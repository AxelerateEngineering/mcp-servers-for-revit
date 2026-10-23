using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Nice3point.TUnit.Revit;
using Nice3point.TUnit.Revit.Executors;
using RevitMCPCommandSet.Models.Views;
using RevitMCPCommandSet.Services.Views;
using TUnit.Core;
using TUnit.Core.Executors;

namespace RevitMCPCommandSet.Tests.Views;

/// <summary>
/// End to end: a side from <see cref="RoomElevationAnalyzer"/> is passed unchanged to
/// <see cref="ElevationViewFactory"/>, the way the AI chains analyze_room_elevations and
/// create_elevation_views. The ground room is 10 x 14 ft (wall centrelines) with one extra wall
/// 3 ft outside each side, which a view clipped at the wall's outer face must not see.
/// </summary>
public class InteriorElevationTests : RevitApiTest
{
    private const double LengthTolerance = 0.01;
    private const double RoomWidth = 10.0;
    private const double RoomDepth = 14.0;
    private const double OuterWallGap = 3.0;
    private const double RaisedLevelElevation = 12.0;
    private const double RaisedRoomBaseOffset = 1.5;

    private static readonly ElevationSide[] AllSides =
        { ElevationSide.North, ElevationSide.East, ElevationSide.South, ElevationSide.West };

    private static Document _doc = null!;
    private static Room _room = null!, _raisedRoom = null!;
    private static Wall _north = null!, _east = null!, _south = null!, _west = null!;
    private static Wall _outerNorth = null!, _outerEast = null!, _outerSouth = null!, _outerWest = null!;

    [Before(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Setup()
    {
        _doc = Application.NewProjectDocument(UnitSystem.Imperial);

        using var tx = new Transaction(_doc, "Setup interior elevation tests");
        var options = tx.GetFailureHandlingOptions();
        options.SetFailuresPreprocessor(new DeleteWarnings());
        tx.SetFailureHandlingOptions(options);
        tx.Start();

        var ground = Level.Create(_doc, 0.0);
        ViewPlan.Create(_doc, FloorPlanTypeId(), ground.Id);
        _south = CreateWall(ground, 0, 0, RoomWidth, 0);
        _east = CreateWall(ground, RoomWidth, 0, RoomWidth, RoomDepth);
        _north = CreateWall(ground, RoomWidth, RoomDepth, 0, RoomDepth);
        _west = CreateWall(ground, 0, RoomDepth, 0, 0);
        _outerSouth = CreateWall(ground, 0, -OuterWallGap, RoomWidth, -OuterWallGap);
        _outerEast = CreateWall(ground, RoomWidth + OuterWallGap, 0, RoomWidth + OuterWallGap, RoomDepth);
        _outerNorth = CreateWall(ground, RoomWidth, RoomDepth + OuterWallGap, 0, RoomDepth + OuterWallGap);
        _outerWest = CreateWall(ground, -OuterWallGap, RoomDepth, -OuterWallGap, 0);
        _doc.Regenerate();
        _room = _doc.Create.NewRoom(ground, new UV(RoomWidth / 2, RoomDepth / 2));

        // A room whose floor is at 12 + 1.5 ft. The base offset must not exceed the computation height.
        var raised = Level.Create(_doc, RaisedLevelElevation);
        raised.get_Parameter(BuiltInParameter.LEVEL_ROOM_COMPUTATION_HEIGHT).Set(4.0);
        ViewPlan.Create(_doc, FloorPlanTypeId(), raised.Id);
        const double x0 = 40;
        CreateWall(raised, x0, 0, x0 + RoomWidth, 0);
        CreateWall(raised, x0 + RoomWidth, 0, x0 + RoomWidth, RoomDepth);
        CreateWall(raised, x0 + RoomWidth, RoomDepth, x0, RoomDepth);
        CreateWall(raised, x0, RoomDepth, x0, 0);
        _doc.Regenerate();
        _raisedRoom = _doc.Create.NewRoom(raised, new UV(x0 + RoomWidth / 2, RoomDepth / 2));
        _raisedRoom.BaseOffset = RaisedRoomBaseOffset;
        _raisedRoom.LimitOffset = 8.0;

        if (tx.Commit() != TransactionStatus.Committed)
            throw new InvalidOperationException("Fixture setup rolled back.");
    }

    [After(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Cleanup()
    {
        _doc?.Close(false);
    }

    /// <summary>
    /// Asks Revit which walls the view shows, so the check does not depend on how
    /// <see cref="View.ViewDirection"/>'s sign relates to the direction the view looks in.
    /// </summary>
    [Test]
    [Arguments("North")]
    [Arguments("East")]
    [Arguments("South")]
    [Arguments("West")]
    public async Task AnalyzedSide_CreatedView_SeesItsWallButNotTheOppositeWallOrBeyondTheFarClip(string sideName)
    {
        var (facing, opposite, outer) = Walls(sideName);

        var result = Create(_room, sideName);

        await Assert.That(result.Error).IsNull();
        var visible = VisibleWallIds(result.ViewId!.Value);
        await Assert.That(visible).Contains(facing.Id);
        await Assert.That(visible).DoesNotContain(opposite.Id);
        await Assert.That(visible).DoesNotContain(outer.Id);
    }

    [Test]
    [Arguments("North")]
    [Arguments("East")]
    [Arguments("South")]
    [Arguments("West")]
    public async Task AnalyzedSide_CreatedView_FarClipIsDistanceToWallFacePlusWallThickness(string sideName)
    {
        var facing = Walls(sideName).Facing;
        var side = AnalyzeSide(_room, sideName);
        var origin = new XYZ(side.Origin[0], side.Origin[1], 0);
        var distanceToFace = ((LocationCurve)facing.Location).Curve.Distance(origin) - facing.Width / 2;
        var expected = distanceToFace + facing.Width;

        var result = Create(_room, sideName);

        await Assert.That(result.Error).IsNull();
        var view = View(result.ViewId!.Value);
        await Assert.That(view.get_Parameter(BuiltInParameter.VIEWER_BOUND_FAR_CLIPPING).AsInteger()).IsEqualTo(2);
        await Assert.That(view.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR).AsDouble())
            .IsEqualTo(expected).Within(LengthTolerance);
    }

    [Test]
    public async Task RaisedRoomWithBaseOffset_CreatedView_CropBottomIsAtTheRoomFloor()
    {
        var side = AnalyzeSide(_raisedRoom, "North");
        var floor = RaisedLevelElevation + RaisedRoomBaseOffset;

        var result = Create(_raisedRoom, "North");

        await Assert.That(result.Error).IsNull();
        var view = View(result.ViewId!.Value);
        await Assert.That(view.CropBoxActive).IsTrue();
        var corners = view.GetCropRegionShapeManager().GetCropShape().Single()
            .Select(c => c.GetEndPoint(0)).ToList();
        var heights = corners.Select(c => c.Z).ToList();
        var across = corners.Select(c => c.DotProduct(view.RightDirection)).ToList();
        await Assert.That(heights.Min()).IsEqualTo(floor).Within(LengthTolerance);
        await Assert.That(heights.Max()).IsEqualTo(floor + _raisedRoom.UnboundedHeight).Within(LengthTolerance);
        await Assert.That(across.Max() - across.Min()).IsEqualTo(side.CropWidth).Within(LengthTolerance);
        await Assert.That(side.CropWidth).IsEqualTo(RoomWidth - _north.Width).Within(LengthTolerance);
    }

    private static (Wall Facing, Wall Opposite, Wall Outer) Walls(string sideName) => sideName switch
    {
        "North" => (_north, _south, _outerNorth),
        "East" => (_east, _west, _outerEast),
        "South" => (_south, _north, _outerSouth),
        "West" => (_west, _east, _outerWest),
        _ => throw new ArgumentOutOfRangeException(nameof(sideName)),
    };

    private static RoomElevationAnalysis Analyze(Room room) =>
        RoomElevationAnalyzer.Analyze(_doc, new[] { room.Id.Value }, AllSides).Rooms.Single();

    private static ElevationSideAnalysis AnalyzeSide(Room room, string sideName) =>
        Analyze(room).Sides.Single(s => s.Side == sideName);

    /// <summary>Analyzes the room and creates the view for one side, copying the side unchanged.</summary>
    private static ElevationViewResult Create(Room room, string sideName)
    {
        var analysis = Analyze(room);
        var side = analysis.Sides.Single(s => s.Side == sideName);
        var request = new ElevationViewRequest
        {
            HostPlanId = analysis.HostPlanId!.Value,
            RoomId = analysis.RoomId,
            Origin = side.Origin,
            ViewDirection = side.ViewDirection,
            CropWidth = side.CropWidth,
            CropHeight = side.CropHeight,
            FarClip = side.FarClip,
            Name = side.SuggestedName,
        };
        return ElevationViewFactory.Create(_doc, new[] { request }, new ElevationViewOptions())[0];
    }

    private static Wall CreateWall(Level level, double x1, double y1, double x2, double y2) =>
        Wall.Create(_doc, Line.CreateBound(new XYZ(x1, y1, level.Elevation), new XYZ(x2, y2, level.Elevation)),
            level.Id, false);

    private static ElementId FloorPlanTypeId() =>
        new FilteredElementCollector(_doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
            .First(t => t.ViewFamily == ViewFamily.FloorPlan).Id;

    private static View View(long id) => (View)_doc.GetElement(new ElementId(id));

    private static List<ElementId> VisibleWallIds(long viewId) =>
        new FilteredElementCollector(_doc, new ElementId(viewId)).OfClass(typeof(Wall)).ToElementIds().ToList();

    private sealed class DeleteWarnings : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            foreach (var message in accessor.GetFailureMessages())
            {
                if (message.GetSeverity() == FailureSeverity.Warning)
                    accessor.DeleteWarning(message);
            }

            return FailureProcessingResult.Continue;
        }
    }
}
