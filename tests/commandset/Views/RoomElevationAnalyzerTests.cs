using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Nice3point.TUnit.Revit;
using Nice3point.TUnit.Revit.Executors;
using RevitMCPCommandSet.Models.Views;
using RevitMCPCommandSet.Services.Views;
using TUnit.Core;
using TUnit.Core.Executors;

namespace RevitMCPCommandSet.Tests.Views;

public class RoomElevationAnalyzerTests : RevitApiTest
{
    private const double DirectionTolerance = 1e-6;
    private const double LengthTolerance = 0.01;
    // Rectangular room, wall centrelines: 10 ft along X, 14 ft along Y, so a width/depth mix-up shows.
    private const double RectWidth = 10.0;
    private const double RectDepth = 14.0;
    private const double RaisedLevelElevation = 12.0;
    private const double RaisedRoomBaseOffset = 1.5;

    private static readonly ElevationSide[] AllSides =
        { ElevationSide.North, ElevationSide.East, ElevationSide.South, ElevationSide.West };

    private static Document _doc = null!;
    private static Level _groundLevel = null!;
    private static Room _rectangularRoom = null!;
    private static Room _raisedRoom = null!;
    private static Room _rotated30Room = null!;
    private static Room _rotated45Room = null!;
    private static Room _triangularRoom = null!;
    private static Room _unplacedRoom = null!;
    private static Level _raisedLevel = null!;
    private static double _wallWidth;

    [Before(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Setup()
    {
        _doc = Application.NewProjectDocument(UnitSystem.Imperial);

        using var tx = new Transaction(_doc, "Setup room elevation analysis tests");
        var failures = new CollectingFailuresPreprocessor();
        var options = tx.GetFailureHandlingOptions();
        options.SetFailuresPreprocessor(failures);
        tx.SetFailureHandlingOptions(options);
        tx.Start();

        _groundLevel = Level.Create(_doc, 0.0);

        // 10 ft x 14 ft (wall centrelines), axis-aligned, centred on (5, 7).
        _rectangularRoom = CreateRoomInRectangle(_groundLevel, new XYZ(RectWidth / 2, RectDepth / 2, 0),
            RectWidth, RectDepth, 0);

        // Raised level with a room base offset: the floor is at 12 + 1.5 ft.
        _raisedLevel = Level.Create(_doc, RaisedLevelElevation);
        // A room's base offset must not be above its level's computation height.
        _raisedLevel.get_Parameter(BuiltInParameter.LEVEL_ROOM_COMPUTATION_HEIGHT).Set(4.0);
        _raisedRoom = CreateRoomInRectangle(_raisedLevel, new XYZ(40, 5, 0), 10, 10, 0);
        _raisedRoom.BaseOffset = RaisedRoomBaseOffset;
        _raisedRoom.LimitOffset = 8.0;

        _rotated30Room = CreateRoomInRectangle(_groundLevel, new XYZ(80, 5, 0), 12, 8, 30);
        _rotated45Room = CreateRoomInRectangle(_groundLevel, new XYZ(110, 5, 0), 10, 10, 45);

        // Right triangle: faces look South, West and (along the hypotenuse) North. Nothing looks East.
        _triangularRoom = CreateRoomInPolygon(_groundLevel,
            new[] { new XYZ(140, 0, 0), new XYZ(160, 0, 0), new XYZ(140, 10, 0) }, new XYZ(145, 3, 0));

        _unplacedRoom = _doc.Create.NewRoom(_doc.Phases.get_Item(_doc.Phases.Size - 1));

        _wallWidth = new FilteredElementCollector(_doc).OfClass(typeof(Wall)).Cast<Wall>().First().Width;

        if (tx.Commit() != TransactionStatus.Committed)
        {
            throw new InvalidOperationException("Fixture setup rolled back: " + string.Join("; ", failures.Errors));
        }
    }

    [After(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Cleanup()
    {
        _doc?.Close(false);
    }

    [Test]
    public async Task Analyze_RectangularRoom_ReturnsFourAxisAlignedSides()
    {
        var room = AnalyzeSingle(_rectangularRoom);

        await Assert.That(room.Error).IsNull();
        await Assert.That(room.Sides.Select(s => s.Side))
            .IsEquivalentTo(new[] { "North", "East", "South", "West" });
        await AssertDirection(Side(room, "North"), 0, 1);
        await AssertDirection(Side(room, "East"), 1, 0);
        await AssertDirection(Side(room, "South"), 0, -1);
        await AssertDirection(Side(room, "West"), -1, 0);
    }

    [Test]
    [Arguments("North", RectDepth / 2)]
    [Arguments("South", RectDepth / 2)]
    [Arguments("East", RectWidth / 2)]
    [Arguments("West", RectWidth / 2)]
    public async Task Analyze_RectangularRoom_FarClipIsDistanceToWallFacePlusWallThickness(
        string sideName, double distanceToCentreline)
    {
        var side = Side(AnalyzeSingle(_rectangularRoom), sideName);

        // The finish face is half a wall closer than the centreline; the far clip reaches it
        // and then the full wall thickness beyond.
        var distanceToFace = distanceToCentreline - _wallWidth / 2;
        await Assert.That(side.FarClip).IsEqualTo(distanceToFace + _wallWidth).Within(LengthTolerance);
    }

    [Test]
    [Arguments("North", RectWidth)]
    [Arguments("South", RectWidth)]
    [Arguments("East", RectDepth)]
    [Arguments("West", RectDepth)]
    public async Task Analyze_RectangularRoom_CropCoversTheRoomFromItsCentreAndFloor(
        string sideName, double wallLengthAcross)
    {
        var side = Side(AnalyzeSingle(_rectangularRoom), sideName);

        await Assert.That(side.Origin[0]).IsEqualTo(RectWidth / 2).Within(LengthTolerance);
        await Assert.That(side.Origin[1]).IsEqualTo(RectDepth / 2).Within(LengthTolerance);
        await Assert.That(side.Origin[2]).IsEqualTo(0).Within(LengthTolerance);
        // Finish face to finish face across the view.
        await Assert.That(side.CropWidth).IsEqualTo(wallLengthAcross - _wallWidth).Within(LengthTolerance);
        await Assert.That(side.CropHeight).IsEqualTo(_rectangularRoom.UnboundedHeight).Within(LengthTolerance);
    }

    [Test]
    public async Task Analyze_RectangularRoom_ReportsRoomIdentityAndSuggestedNames()
    {
        var room = AnalyzeSingle(_rectangularRoom);

        await Assert.That(room.RoomId).IsEqualTo(_rectangularRoom.Id.Value);
        await Assert.That(room.LevelId).IsEqualTo(_groundLevel.Id.Value);
        await Assert.That(room.HostPlanId).IsNull();
        await Assert.That(Side(room, "East").SuggestedName)
            .IsEqualTo($"{_rectangularRoom.Number} {room.Name} - East");
    }

    [Test]
    public async Task Analyze_RequestedSidesOnly_ReturnsOnlyThoseSides()
    {
        var room = AnalyzeSingle(_rectangularRoom, new[] { ElevationSide.North, ElevationSide.East });

        await Assert.That(room.Sides.Select(s => s.Side)).IsEquivalentTo(new[] { "North", "East" });
        await Assert.That(room.SkippedSides).IsEmpty();
    }

    [Test]
    public async Task Analyze_RoomRotated30Degrees_ViewDirectionsRotateWithTheRoom()
    {
        var room = AnalyzeSingle(_rotated30Room);
        var cos = Math.Cos(Math.PI / 6);
        var sin = Math.Sin(Math.PI / 6);

        await Assert.That(room.Sides).HasCount(4);
        await AssertDirection(Side(room, "East"), cos, sin);
        await AssertDirection(Side(room, "North"), -sin, cos);
        await AssertDirection(Side(room, "West"), -cos, -sin);
        await AssertDirection(Side(room, "South"), sin, -cos);
    }

    [Test]
    public async Task Analyze_RoomRotatedExactly45Degrees_ReturnsFourSides()
    {
        var room = AnalyzeSingle(_rotated45Room);
        var half = Math.Sqrt(0.5);

        await Assert.That(room.Sides.Select(s => s.Side))
            .IsEquivalentTo(new[] { "North", "East", "South", "West" });
        await AssertDirection(Side(room, "North"), half, half);
        await AssertDirection(Side(room, "West"), -half, half);
        await AssertDirection(Side(room, "South"), -half, -half);
        await AssertDirection(Side(room, "East"), half, -half);
    }

    [Test]
    public async Task Analyze_TriangularRoom_SkipsTheSideWithNoSideFace()
    {
        var room = AnalyzeSingle(_triangularRoom);

        await Assert.That(room.Error).IsNull();
        await Assert.That(room.Sides.Select(s => s.Side)).IsEquivalentTo(new[] { "North", "South", "West" });
        await Assert.That(room.SkippedSides.Select(s => s.Side)).IsEquivalentTo(new[] { "East" });
        await Assert.That(room.SkippedSides.Single().Reason).IsNotNull().And.IsNotEmpty();
    }

    [Test]
    public async Task Analyze_RoomOnRaisedLevelWithBaseOffset_OriginIsAtTheRoomFloor()
    {
        var room = AnalyzeSingle(_raisedRoom);

        await Assert.That(room.Error).IsNull();
        await Assert.That(room.LevelId).IsEqualTo(_raisedLevel.Id.Value);
        await Assert.That(room.Sides).HasCount(4);
        foreach (var side in room.Sides)
        {
            await Assert.That(side.Origin[2])
                .IsEqualTo(RaisedLevelElevation + RaisedRoomBaseOffset).Within(LengthTolerance);
        }
    }

    [Test]
    public async Task Analyze_BadIds_ReturnPerRoomErrorsAndTheOtherRoomsStillSucceed()
    {
        var wallId = new FilteredElementCollector(_doc).OfClass(typeof(Wall)).FirstElementId().Value;
        var ids = new[] { _rectangularRoom.Id.Value, 999_999_999L, -5L, wallId, _unplacedRoom.Id.Value };

        var rooms = RoomElevationAnalyzer.Analyze(_doc, ids, AllSides).Rooms;

        // One result per id, in input order.
        await Assert.That(string.Join(",", rooms.Select(r => r.RoomId))).IsEqualTo(string.Join(",", ids));
        await Assert.That(rooms[0].Error).IsNull();
        await Assert.That(rooms[0].Sides).HasCount(4);
        foreach (var failed in rooms.Skip(1))
        {
            await Assert.That(failed.Error).IsNotNull().And.IsNotEmpty();
            await Assert.That(failed.Sides).IsEmpty();
        }
    }

    private static RoomElevationAnalysis AnalyzeSingle(Room room, IReadOnlyCollection<ElevationSide>? sides = null)
    {
        var result = RoomElevationAnalyzer.Analyze(_doc, new[] { room.Id.Value }, sides ?? AllSides);
        return result.Rooms.Single();
    }

    private static ElevationSideAnalysis Side(RoomElevationAnalysis room, string side) =>
        room.Sides.Single(s => s.Side == side);

    private static async Task AssertDirection(ElevationSideAnalysis side, double dx, double dy)
    {
        await Assert.That(side.ViewDirection[0]).IsEqualTo(dx).Within(DirectionTolerance);
        await Assert.That(side.ViewDirection[1]).IsEqualTo(dy).Within(DirectionTolerance);
    }

    /// <summary>
    /// Builds four walls whose centrelines form a width x depth rectangle centred on <paramref name="centre"/>,
    /// rotated by <paramref name="rotationDegrees"/> about Z, and places a room inside.
    /// </summary>
    private static Room CreateRoomInRectangle(Level level, XYZ centre, double width, double depth,
        double rotationDegrees)
    {
        var rotation = Transform.CreateRotationAtPoint(XYZ.BasisZ, rotationDegrees * Math.PI / 180, centre);
        var corners = new[]
            {
                new XYZ(-width / 2, -depth / 2, 0), new XYZ(width / 2, -depth / 2, 0),
                new XYZ(width / 2, depth / 2, 0), new XYZ(-width / 2, depth / 2, 0)
            }
            .Select(c => rotation.OfPoint(new XYZ(centre.X + c.X, centre.Y + c.Y, level.Elevation)))
            .ToArray();

        return CreateRoomInPolygon(level, corners, centre);
    }

    /// <summary>Deletes warnings so the fixture commits; records errors so a failed setup says why.</summary>
    private sealed class CollectingFailuresPreprocessor : IFailuresPreprocessor
    {
        public List<string> Errors { get; } = new();

        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            foreach (var message in accessor.GetFailureMessages())
            {
                if (message.GetSeverity() == FailureSeverity.Warning)
                {
                    accessor.DeleteWarning(message);
                }
                else
                {
                    Errors.Add(message.GetDescriptionText());
                }
            }

            return FailureProcessingResult.Continue;
        }
    }

    private static Room CreateRoomInPolygon(Level level, IReadOnlyList<XYZ> corners, XYZ pointInside)
    {
        for (var i = 0; i < corners.Count; i++)
        {
            var start = corners[i];
            var end = corners[(i + 1) % corners.Count];
            Wall.Create(_doc, Line.CreateBound(start, end), level.Id, false);
        }

        _doc.Regenerate();
        return _doc.Create.NewRoom(level, new UV(pointInside.X, pointInside.Y));
    }
}
