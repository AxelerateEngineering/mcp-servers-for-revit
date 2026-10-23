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
                analysis.HostPlanId = HostPlanService.FindHostPlan(doc, room.LevelId)?.Id.GetValue();
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
            if (!RequestIds.IsValid(roomId))
            {
                error = $"Invalid room id {roomId}: ids must be positive safe integers";
                return null;
            }

            var element = RequestIds.GetElement(doc, roomId);
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
                    Side = side,
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
            var skipped = new Dictionary<SkippedFaceKind, int>();

            foreach (Face face in solid.Faces)
            {
                var sideFace = ToSideFace(face, centroid, out var skipKind);
                if (sideFace != null)
                {
                    sideFaces.Add(sideFace);
                }
                else if (skipKind != null)
                {
                    skipped[skipKind.Value] = skipped.TryGetValue(skipKind.Value, out var count) ? count + 1 : 1;
                }
            }

            foreach (var entry in skipped)
            {
                warnings.Add(SkippedFacesWarning(entry.Key, entry.Value));
            }

            return sideFaces;
        }

        /// <summary>E.g. "Skipped 2 curved faces with no usable chord".</summary>
        private static string SkippedFacesWarning(SkippedFaceKind kind, int count)
        {
            var faces = count == 1 ? "face" : "faces";
            return kind.Detail == null
                ? $"Skipped {count} {kind.Adjective} {faces}"
                : $"Skipped {count} {kind.Adjective} {faces} {kind.Detail}";
        }

        /// <summary>
        /// Returns the face as a Side Face, or null. A null with a null kind means the face is a
        /// horizontal top or bottom face, which is ignored silently.
        /// </summary>
        private static SideFace ToSideFace(Face face, XYZ centroid, out SkippedFaceKind? skipKind)
        {
            skipKind = null;

            if (face is PlanarFace planar)
            {
                var normal = planar.FaceNormal.Normalize();
                if (Math.Abs(normal.Z) >= HorizontalFaceMinNormalZ)
                {
                    return null;
                }

                if (Math.Abs(normal.Z) >= VerticalFaceMaxNormalZ)
                {
                    skipKind = new SkippedFaceKind("sloped");
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
                    skipKind = new SkippedFaceKind("non-vertical curved");
                    return null;
                }

                if (!TryGetChord(face, out var start, out var end))
                {
                    skipKind = new SkippedFaceKind("curved", "with no usable chord");
                    return null;
                }

                var chordMidpoint = (start + end) / 2;
                var outward = new XYZ(chordMidpoint.X - centroid.X, chordMidpoint.Y - centroid.Y, 0);
                if (outward.GetLength() <= VerticalAxisTolerance)
                {
                    skipKind = new SkippedFaceKind("curved", "centred on the room");
                    return null;
                }

                return new SideFace(face, outward.Normalize(), start.DistanceTo(end), FacePoints(face));
            }

            skipKind = new SkippedFaceKind("unsupported", $"({face.GetType().Name})");
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
            analysis.SkippedSides.Add(new SkippedElevationSide { Side = side, Reason = reason });
        }

        private static string SuggestedName(RoomElevationAnalysis analysis, ElevationSide side)
        {
            var label = string.Join(" ", new[] { analysis.Number, analysis.Name }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
            return $"{label} - {side}";
        }

        /// <summary>Rounds away floating-point noise (and negative zero) so the AI sees clean values.</summary>
        private static double Round(double value) => Math.Round(value, OutputDecimals) + 0.0;

        /// <summary>Why a face is not a Side Face; equal kinds are counted together in one warning.</summary>
        private readonly struct SkippedFaceKind : IEquatable<SkippedFaceKind>
        {
            public SkippedFaceKind(string adjective, string detail = null)
            {
                Adjective = adjective;
                Detail = detail;
            }

            public string Adjective { get; }
            public string Detail { get; }

            public bool Equals(SkippedFaceKind other) => Adjective == other.Adjective && Detail == other.Detail;
            public override bool Equals(object obj) => obj is SkippedFaceKind other && Equals(other);
            public override int GetHashCode() => (Adjective, Detail).GetHashCode();
        }

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
