using Autodesk.Revit.DB.Architecture;
using RevitMCPCommandSet.Models.Views;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services.Views
{
    /// <summary>
    /// Creates Interior Elevations from requests (normally the sides returned by analyze_room_elevations).
    /// Each view is created in its own transaction: an item either commits completely or leaves
    /// neither a view nor a marker behind. The elevation marker is an internal detail.
    /// </summary>
    public static class ElevationViewFactory
    {
        public const int MinScale = 1;
        public const int MaxScale = 24000;

        private const int MarkerIndex = 0;

        // BackClippingParam.ClipNoLine. The enum only exists from Revit 2024, so the integer is used.
        private const int ClipWithoutLine = 2;

        private const double DirectionTolerance = 1e-6;

        /// <summary>
        /// Creates one view per request and returns one result per request, in input order.
        /// Throws <see cref="ArgumentException"/> before creating anything when the view type
        /// or template name is unknown; the message lists the valid names.
        /// </summary>
        public static List<ElevationViewResult> Create(
            Document doc, IReadOnlyList<ElevationViewRequest> requests, ElevationViewOptions options)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (requests == null) throw new ArgumentNullException(nameof(requests));
            options = options ?? new ElevationViewOptions();

            var batch = new Batch(
                ResolveViewType(doc, options.ViewTypeName),
                ResolveTemplate(doc, options.ViewTemplateName),
                options.Scale,
                new ViewNameRegistry(doc));

            var results = new List<ElevationViewResult>(requests.Count);
            for (var i = 0; i < requests.Count; i++)
            {
                results.Add(CreateOne(doc, i, requests[i], batch));
            }
            return results;
        }

        private static ElevationViewResult CreateOne(Document doc, int index, ElevationViewRequest request, Batch batch)
        {
            var result = new ElevationViewResult { Index = index };

            result.Error = Validate(request, batch.Scale);
            if (result.Error != null)
                return result;

            if (!(RequestIds.GetElement(doc, request.HostPlanId) is ViewPlan hostPlan) || hostPlan.IsTemplate)
            {
                result.Error = $"hostPlanId {request.HostPlanId} is not a plan view in the active document.";
                return result;
            }

            var origin = new XYZ(request.Origin[0], request.Origin[1], request.Origin[2]);
            var look = new XYZ(request.ViewDirection[0], request.ViewDirection[1], 0).Normalize();
            var name = batch.Names.Propose(request.Name);
            ViewSection view = null;

            result.Error = ElevationFailurePreprocessor.RunTransaction(doc, "Create Interior Elevation", () =>
            {
                var marker = ElevationMarker.CreateElevationMarker(doc, batch.ViewType.Id, origin, batch.Scale);
                view = marker.CreateElevation(doc, hostPlan.Id, MarkerIndex);

                if (batch.Template != null)
                    view.ViewTemplateId = batch.Template.Id;
                if (!batch.Control.Scale)
                    view.Scale = batch.Scale;

                doc.Regenerate();
                PointMarker(doc, marker, view, origin, look);

                if (batch.Control.FarClip)
                    result.Warnings.Add(batch.Control.SkippedWarning("far clip", "farClip"));
                else
                    SetFarClip(view, request.FarClip);

                if (batch.Control.Crop)
                    result.Warnings.Add(batch.Control.SkippedWarning("crop", "cropWidth and cropHeight"));
                else
                    SetCrop(doc, view, origin, request.CropWidth, request.CropHeight);

                if (request.RoomId.HasValue)
                    CopyRoomPhase(doc, view, request.RoomId.Value, result.Warnings);

                view.Name = name;
            }, result.Warnings);

            if (result.Error != null)
                return result;

            batch.Names.Reserve(name);
            result.ViewId = view.Id.GetValue();
            result.FinalName = view.Name;
            return result;
        }

        private static string Validate(ElevationViewRequest request, int scale)
        {
            if (request == null)
                return "The item is missing.";
            if (request.InputError != null)
                return request.InputError;
            if (!RequestIds.IsValid(request.HostPlanId))
                return "hostPlanId must be a positive safe integer.";
            if (request.RoomId.HasValue && !RequestIds.IsValid(request.RoomId.Value))
                return "roomId must be a positive safe integer.";
            if (request.Origin == null || request.Origin.Length != 3 || !request.Origin.All(IsFinite))
                return "origin must be [x, y, z] with finite numbers.";
            if (request.ViewDirection == null || request.ViewDirection.Length != 2 || !request.ViewDirection.All(IsFinite))
                return "viewDirection must be [dx, dy] with finite numbers.";
            if (Math.Sqrt(request.ViewDirection[0] * request.ViewDirection[0]
                          + request.ViewDirection[1] * request.ViewDirection[1]) < 1e-9)
                return "viewDirection must not be zero.";
            if (!IsPositive(request.CropWidth))
                return "cropWidth must be a finite number greater than 0.";
            if (!IsPositive(request.CropHeight))
                return "cropHeight must be a finite number greater than 0.";
            if (!IsPositive(request.FarClip))
                return "farClip must be a finite number greater than 0.";
            if (string.IsNullOrWhiteSpace(request.Name))
                return "name must not be empty.";
            if (scale < MinScale || scale > MaxScale)
                return $"scale must be an integer from {MinScale} to {MaxScale}.";
            return null;
        }

        /// <summary>
        /// Rotates the marker about a vertical axis through the origin until the view looks along
        /// <paramref name="look"/>. The look direction is -ViewDirection, read from the real view.
        /// </summary>
        private static void PointMarker(Document doc, ElevationMarker marker, View view, XYZ origin, XYZ look)
        {
            var current = LookDirection(view);
            var angle = Math.Atan2(current.X * look.Y - current.Y * look.X, current.DotProduct(look));
            if (Math.Abs(angle) > 1e-9)
            {
                ElementTransformUtils.RotateElement(doc, marker.Id, Line.CreateBound(origin, origin + XYZ.BasisZ), angle);
                doc.Regenerate();
            }

            if (LookDirection(view).DotProduct(look) < 1 - DirectionTolerance)
                throw new InvalidOperationException("The view could not be turned to the requested viewDirection.");
        }

        private static XYZ LookDirection(View view)
        {
            var toViewer = view.ViewDirection;
            return new XYZ(-toViewer.X, -toViewer.Y, 0).Normalize();
        }

        private static void SetFarClip(View view, double farClip)
        {
            SetParameter(view, BuiltInParameter.VIEWER_BOUND_FAR_CLIPPING, p => p.Set(ClipWithoutLine), "far clipping");
            SetParameter(view, BuiltInParameter.VIEWER_BOUND_OFFSET_FAR, p => p.Set(farClip), "far clip offset");
        }

        /// <summary>
        /// Sets a rectangular crop in the view plane: cropWidth wide, centred on the origin,
        /// from origin.Z up by cropHeight. The crop must be active (and regenerated) before
        /// Revit accepts a crop shape.
        /// </summary>
        private static void SetCrop(Document doc, View view, XYZ origin, double width, double height)
        {
            view.CropBoxActive = true;
            doc.Regenerate();

            // Project the origin onto the view plane so the rectangle is coplanar with the view.
            var normal = view.ViewDirection;
            var centre = origin - normal.Multiply((origin - view.Origin).DotProduct(normal));
            var halfWidth = view.RightDirection.Multiply(width / 2);
            var up = view.UpDirection.Multiply(height);

            var bottomLeft = centre - halfWidth;
            var bottomRight = centre + halfWidth;
            var topRight = bottomRight + up;
            var topLeft = bottomLeft + up;
            var loop = CurveLoop.Create(new List<Curve>
            {
                Line.CreateBound(bottomLeft, bottomRight),
                Line.CreateBound(bottomRight, topRight),
                Line.CreateBound(topRight, topLeft),
                Line.CreateBound(topLeft, bottomLeft),
            });

            var shapeManager = view.GetCropRegionShapeManager();
            if (!shapeManager.CanHaveShape || !shapeManager.IsCropRegionShapeValid(loop))
                throw new InvalidOperationException("Revit rejected the crop rectangle for this view.");
            shapeManager.SetCropShape(loop);
        }

        private static void CopyRoomPhase(Document doc, View view, long roomId, List<string> warnings)
        {
            const string keepsDefault = "the view keeps its default phase";
            try
            {
                if (!(RequestIds.GetElement(doc, roomId) is Room room))
                {
                    warnings.Add($"roomId {roomId} is not a Room; {keepsDefault}.");
                    return;
                }

                var phaseId = room.get_Parameter(BuiltInParameter.ROOM_PHASE)?.AsElementId();
                var viewPhase = view.get_Parameter(BuiltInParameter.VIEW_PHASE);
                if (phaseId == null || phaseId == ElementId.InvalidElementId
                    || viewPhase == null || viewPhase.IsReadOnly || !viewPhase.Set(phaseId))
                {
                    warnings.Add($"The Room's phase could not be copied; {keepsDefault}.");
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"The Room's phase could not be copied ({ex.Message}); {keepsDefault}.");
            }
        }

        private static void SetParameter(Element element, BuiltInParameter id, Func<Parameter, bool> set, string label)
        {
            var parameter = element.get_Parameter(id);
            if (parameter == null || parameter.IsReadOnly || !set(parameter))
                throw new InvalidOperationException($"The view's {label} could not be set.");
        }

        private static ViewFamilyType ResolveViewType(Document doc, string name)
        {
            var types = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .Where(t => t.ViewFamily == ViewFamily.Elevation)
                .ToList();

            if (string.IsNullOrEmpty(name))
                return types.FirstOrDefault()
                       ?? throw new InvalidOperationException("The document has no Elevation view type.");

            return types.FirstOrDefault(t => t.Name == name)
                   ?? throw new ArgumentException(
                       $"Unknown viewTypeName \"{name}\". Valid Elevation view types: {QuotedList(types.Select(t => t.Name))}. Nothing was created.");
        }

        private static View ResolveTemplate(Document doc, string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            var templates = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate && v.ViewType == ViewType.Elevation)
                .ToList();

            return templates.FirstOrDefault(t => t.Name == name)
                   ?? throw new ArgumentException(
                       $"Unknown viewTemplateName \"{name}\". Valid elevation view templates: {QuotedList(templates.Select(t => t.Name))}. Nothing was created.");
        }

        private static string QuotedList(IEnumerable<string> names)
        {
            var list = names.Select(n => $"\"{n}\"").ToList();
            return list.Count == 0 ? "(none)" : string.Join(", ", list);
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static bool IsPositive(double value) => IsFinite(value) && value > 0;

        /// <summary>Everything that is the same for every view in one call.</summary>
        private sealed class Batch
        {
            public Batch(ViewFamilyType viewType, View template, int scale, ViewNameRegistry names)
            {
                ViewType = viewType;
                Template = template;
                Control = TemplateControl.Of(template);
                Scale = scale;
                Names = names;
            }

            public ViewFamilyType ViewType { get; }
            public View Template { get; }
            public TemplateControl Control { get; }
            public int Scale { get; }
            public ViewNameRegistry Names { get; }
        }

        /// <summary>Which of the settings this tool makes are controlled by the chosen view template.</summary>
        private sealed class TemplateControl
        {
            private string _templateName;

            public bool Crop { get; private set; }
            public bool FarClip { get; private set; }
            public bool Scale { get; private set; }

            public static TemplateControl Of(View template)
            {
                if (template == null)
                    return new TemplateControl();

                var controlled = new HashSet<ElementId>(template.GetTemplateParameterIds());
                controlled.ExceptWith(template.GetNonControlledTemplateParameterIds());
                bool Controls(params BuiltInParameter[] parameters) =>
                    parameters.Any(p => controlled.Contains(new ElementId(p)));

                return new TemplateControl
                {
                    _templateName = template.Name,
                    // Every parameter that can switch the crop on or off or set its shape. In Revit 2026 an
                    // elevation template controls none of them (only far clipping, scale and graphics), so this
                    // only matters for Revit versions or templates that expose them.
                    Crop = Controls(BuiltInParameter.VIEWER_CROP_REGION, BuiltInParameter.VIEWER_CROP_REGION_DISABLED,
                        BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP),
                    FarClip = Controls(BuiltInParameter.VIEWER_BOUND_FAR_CLIPPING, BuiltInParameter.VIEWER_BOUND_OFFSET_FAR),
                    Scale = Controls(BuiltInParameter.VIEW_SCALE, BuiltInParameter.VIEW_SCALE_PULLDOWN_IMPERIAL,
                        BuiltInParameter.VIEW_SCALE_PULLDOWN_METRIC),
                };
            }

            public string SkippedWarning(string setting, string requestFields) =>
                $"View template \"{_templateName}\" controls the {setting}, so the view keeps the template's {setting} and {requestFields} were not applied.";
        }
    }
}
