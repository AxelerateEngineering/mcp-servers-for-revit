using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using RevitMCPCommandSet.Models.Views;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services.Views
{
    /// <summary>
    /// Works out, for each Room, the Interior Elevation it should get on each Elevation Side.
    /// Read-only: it never starts a transaction.
    /// </summary>
    public static class RoomElevationAnalyzer
    {
        private const double VerticalFaceMaxNormalZ = 0.01;
        private const double HorizontalFaceMinNormalZ = 0.99995;
        private const double VerticalAxisTolerance = 1e-6;
        private const int AngleDecimals = 6;
        private const int OutputDecimals = 9;
        private const double MinVolume = 1e-9;

        public static RoomElevationAnalysisResult Analyze(
            Document doc, IEnumerable<long> roomIds, IReadOnlyCollection<ElevationSide> sides)
        {
            var calculator = new SpatialElementGeometryCalculator(doc, new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish,
                StoreFreeBoundaryFaces = true
            });
            var requestedSides = sides.Distinct().ToList();

            var result = new RoomElevationAnalysisResult();
            foreach (var roomId in roomIds)
            {
                result.Rooms.Add(AnalyzeRoom(doc, calculator, roomId, requestedSides));
            }

            return result;
        }

        private static RoomElevationAnalysis AnalyzeRoom(
            Document doc, SpatialElementGeometryCalculator calculator, long roomId,
            IReadOnlyList<ElevationSide> sides)
        {
            var analysis = new RoomElevationAnalysis { RoomId = roomId };

            var room = FindRoom(doc, roomId, out var lookupError);
            if (room == null)
            {
                analysis.Error = lookupError;
                return analysis;
            }

            analysis.Number = room.Number;
            analysis.Name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString();
            if (room.LevelId != null && room.LevelId != ElementId.InvalidElementId)
            {
                analysis.LevelId = room.LevelId.GetValue();
                analysis.HostPlanId = FindHostPlan(doc, room.LevelId)?.Id.GetValue();
            }

            if (room.Location == null)
            {
                analysis.Error = "Room is not placed";
                return analysis;
            }

            if (room.Area <= 0)
            {
                analysis.Error = "Room is not enclosed";
                return analysis;
            }

            SpatialElementGeometryResults geometry;
            Solid solid;
            try
            {
                geometry = calculator.CalculateSpatialElementGeometry(room);
                solid = geometry.GetGeometry();
            }
            catch (Exception ex)
            {
                analysis.Error = $"Room geometry could not be calculated: {ex.Message}";
                return analysis;
            }

            if (solid == null || solid.Volume <= MinVolume)
            {
                analysis.Error = "Room has zero volume";
                return analysis;
            }

            AnalyzeSides(doc, geometry, solid, sides, analysis);
            return analysis;
        }

        private static Room FindRoom(Document doc, long roomId, out string error)
        {
            error = null;
            if (roomId <= 0)
            {
                error = $"Invalid room id {roomId}: ids must be positive";
                return null;
            }

            ElementId elementId;
            try
            {
                elementId = roomId.ToRevitElementId();
            }
            catch (OverflowException)
            {
                error = $"Invalid room id {roomId}: too large for this Revit version";
                return null;
            }

            var element = doc.GetElement(elementId);
            if (element == null)
            {
                error = $"No element with id {roomId}";
                return null;
            }

            if (!(element is Room room))
            {
                error = $"Element {roomId} is not a Room";
                return null;
            }

            return room;
        }

        private static View FindHostPlan(Document doc, ElementId levelId)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .FirstOrDefault(v => v.ViewType == ViewType.FloorPlan
                                     && !v.IsTemplate
                                     && v.GenLevel != null
                                     && v.GenLevel.Id == levelId);
        }

        private static void AnalyzeSides(
            Document doc, SpatialElementGeometryResults geometry, Solid solid,
            IReadOnlyList<ElevationSide> sides, RoomElevationAnalysis analysis)
        {
            var solidPoints = solid.Edges.Cast<Edge>().SelectMany(e => e.Tessellate()).ToList();
            var minZ = solidPoints.Min(p => p.Z);
            var maxZ = solidPoints.Max(p => p.Z);
            var centroid = solid.ComputeCentroid();
            var origin = new XYZ(centroid.X, centroid.Y, minZ);

            var sideFaces = CollectSideFaces(solid, centroid, analysis.Warnings);

            foreach (var side in sides)
            {
                var facesOnSide = sideFaces.Where(f => f.Side == side).ToList();
                if (facesOnSide.Count == 0)
                {
                    Skip(analysis, side, "No usable Side Face on this side");
                    continue;
                }

                var longest = facesOnSide.OrderByDescending(f => f.Length).First();
                var direction = longest.Direction;

                var depth = facesOnSide.SelectMany(f => f.Points).Max(p => (p - origin).DotProduct(direction));
                if (depth <= 0)
                {
                    Skip(analysis, side, "Side Faces on this side are not in front of the room centre");
                    continue;
                }

                var across = new XYZ(-direction.Y, direction.X, 0);
                var offsets = solidPoints.Select(p => (p - origin).DotProduct(across)).ToList();
                var halfWidth = Math.Max(Math.Abs(offsets.Min()), Math.Abs(offsets.Max()));

                analysis.Sides.Add(new ElevationSideAnalysis
                {
                    Side = side.ToString(),
                    Origin = new[] { Round(origin.X), Round(origin.Y), Round(origin.Z) },
                    ViewDirection = new[] { Round(direction.X), Round(direction.Y) },
                    CropWidth = Round(2 * halfWidth),
                    CropHeight = Round(maxZ - minZ),
                    FarClip = Round(depth + WallThicknessAt(doc, geometry, longest.Face)),
                    SuggestedName = SuggestedName(analysis, side)
                });
            }
        }

        private static List<SideFace> CollectSideFaces(Solid solid, XYZ centroid, List<string> warnings)
        {
            var sideFaces = new List<SideFace>();
            var skipped = new Dictionary<string, int>();

            foreach (Face face in solid.Faces)
            {
                string skipReason;
                var sideFace = ToSideFace(face, centroid, out skipReason);
                if (sideFace != null)
                {
                    sideFaces.Add(sideFace);
                }
                else if (skipReason != null)
                {
                    skipped[skipReason] = skipped.TryGetValue(skipReason, out var count) ? count + 1 : 1;
                }
            }

            foreach (var entry in skipped)
            {
                warnings.Add($"Skipped {entry.Value} " + string.Format(entry.Key, entry.Value == 1 ? "face" : "faces"));
            }

            return sideFaces;
        }

        /// <summary>
        /// Returns the face as a Side Face, or null. A null with a null reason means the face is a
        /// horizontal top or bottom face, which is ignored silently.
        /// </summary>
        private static SideFace ToSideFace(Face face, XYZ centroid, out string skipReason)
        {
            skipReason = null;

            if (face is PlanarFace planar)
            {
                var normal = planar.FaceNormal.Normalize();
                if (Math.Abs(normal.Z) >= HorizontalFaceMinNormalZ)
                {
                    return null;
                }

                if (Math.Abs(normal.Z) >= VerticalFaceMaxNormalZ)
                {
                    skipReason = "sloped {0}";
                    return null;
                }

                var points = FacePoints(face);
                var direction = new XYZ(normal.X, normal.Y, 0).Normalize();
                var along = new XYZ(-direction.Y, direction.X, 0);
                var offsets = points.Select(p => p.DotProduct(along)).ToList();
                return new SideFace(face, direction, offsets.Max() - offsets.Min(), points);
            }

            if (face is CylindricalFace cylinder)
            {
                if (cylinder.Axis.CrossProduct(XYZ.BasisZ).GetLength() > VerticalAxisTolerance)
                {
                    skipReason = "non-vertical curved {0}";
                    return null;
                }

                if (!TryGetChord(face, out var start, out var end))
                {
                    skipReason = "curved {0} with no usable chord";
                    return null;
                }

                var chordMidpoint = (start + end) / 2;
                var outward = new XYZ(chordMidpoint.X - centroid.X, chordMidpoint.Y - centroid.Y, 0);
                if (outward.GetLength() <= VerticalAxisTolerance)
                {
                    skipReason = "curved {0} centred on the room";
                    return null;
                }

                return new SideFace(face, outward.Normalize(), start.DistanceTo(end), FacePoints(face));
            }

            skipReason = "unsupported {0} (" + face.GetType().Name + ")";
            return null;
        }

        /// <summary>
        /// The XY end points of a vertical curved face: the ends of one of its horizontal arcs.
        /// </summary>
        private static bool TryGetChord(Face face, out XYZ start, out XYZ end)
        {
            start = null;
            end = null;
            foreach (EdgeArray loop in face.EdgeLoops)
            {
                foreach (Edge edge in loop)
                {
                    var curve = edge.AsCurve();
                    if (curve is Line || !curve.IsBound)
                    {
                        continue;
                    }

                    var p0 = curve.GetEndPoint(0);
                    var p1 = curve.GetEndPoint(1);
                    var a = new XYZ(p0.X, p0.Y, 0);
                    var b = new XYZ(p1.X, p1.Y, 0);
                    if (a.IsAlmostEqualTo(b))
                    {
                        continue;
                    }

                    start = a;
                    end = b;
                    return true;
                }
            }

            return false;
        }

        private static List<XYZ> FacePoints(Face face)
        {
            var points = new List<XYZ>();
            foreach (EdgeArray loop in face.EdgeLoops)
            {
                foreach (Edge edge in loop)
                {
                    points.AddRange(edge.Tessellate());
                }
            }

            return points;
        }

        /// <summary>
        /// The thickness of the wall that bounds <paramref name="face"/> at the face's midpoint;
        /// 0 when no wall bounds it there (e.g. a room separation line).
        /// </summary>
        private static double WallThicknessAt(Document doc, SpatialElementGeometryResults geometry, Face face)
        {
            try
            {
                var box = face.GetBoundingBox();
                var midpoint = face.Evaluate(new UV((box.Min.U + box.Max.U) / 2, (box.Min.V + box.Max.V) / 2));

                var nearest = geometry.GetBoundaryFaceInfo(face)
                    .OrderBy(subface => subface.GetSubface()?.Project(midpoint)?.Distance ?? double.MaxValue)
                    .FirstOrDefault();
                var hostId = nearest?.SpatialBoundaryElement?.HostElementId;
                if (hostId == null || hostId == ElementId.InvalidElementId)
                {
                    return 0;
                }

                return doc.GetElement(hostId) is Wall wall ? wall.Width : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static ElevationSide Classify(XYZ direction)
        {
            var degrees = Math.Atan2(direction.Y, direction.X) * 180 / Math.PI;
            if (degrees < 0)
            {
                degrees += 360;
            }

            degrees = Math.Round(degrees, AngleDecimals);
            if (degrees >= 360)
            {
                degrees -= 360;
            }

            if (degrees >= 45 && degrees < 135) return ElevationSide.North;
            if (degrees >= 135 && degrees < 225) return ElevationSide.West;
            if (degrees >= 225 && degrees < 315) return ElevationSide.South;
            return ElevationSide.East;
        }

        private static void Skip(RoomElevationAnalysis analysis, ElevationSide side, string reason)
        {
            analysis.SkippedSides.Add(new SkippedElevationSide { Side = side.ToString(), Reason = reason });
        }

        private static string SuggestedName(RoomElevationAnalysis analysis, ElevationSide side)
        {
            var label = string.Join(" ", new[] { analysis.Number, analysis.Name }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
            return $"{label} - {side}";
        }

        /// <summary>Rounds away floating-point noise (and negative zero) so the AI sees clean values.</summary>
        private static double Round(double value) => Math.Round(value, OutputDecimals) + 0.0;

        private sealed class SideFace
        {
            public SideFace(Face face, XYZ direction, double length, List<XYZ> points)
            {
                Face = face;
                Direction = direction;
                Length = length;
                Points = points;
                Side = Classify(direction);
            }

            public Face Face { get; }
            public XYZ Direction { get; }
            public double Length { get; }
            public List<XYZ> Points { get; }
            public ElevationSide Side { get; }
        }
    }
}
