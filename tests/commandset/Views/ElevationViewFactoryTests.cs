using System.Diagnostics;
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
/// Room fixture: walls on the rectangle (0,0)-(20,10), a Room inside it, and one extra wall
/// further north at y = 30 that a correctly clipped North view must not see.
/// </summary>
public class ElevationViewFactoryTests : RevitApiTest
{
    private const double RoomWidth = 20.0;
    private const double RoomDepth = 10.0;
    private const double CropHeight = 8.0;
    // Plain doubles: a static XYZ would be built before the Revit runtime is loaded.
    private const double CentreX = 10.0, CentreY = 5.0;

    private static Document _doc = null!;
    private static ViewPlan _hostPlan = null!;
    private static Room _room = null!;
    private static Wall _northWall = null!, _eastWall = null!, _southWall = null!, _westWall = null!, _farNorthWall = null!;
    private static View _elevationTemplate = null!;

    [Before(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Setup()
    {
        _doc = Application.NewProjectDocument(UnitSystem.Imperial);

        using var tx = new Transaction(_doc, "Setup Elevation Factory Tests");
        tx.Start();

        var level = Level.Create(_doc, 0.0);
        level.Name = "Elevation Test Level";
        var floorPlanType = new FilteredElementCollector(_doc)
            .OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
            .First(t => t.ViewFamily == ViewFamily.FloorPlan);
        _hostPlan = ViewPlan.Create(_doc, floorPlanType.Id, level.Id);

        _southWall = Wall.Create(_doc, Line.CreateBound(new XYZ(0, 0, 0), new XYZ(RoomWidth, 0, 0)), level.Id, false);
        _eastWall = Wall.Create(_doc, Line.CreateBound(new XYZ(RoomWidth, 0, 0), new XYZ(RoomWidth, RoomDepth, 0)), level.Id, false);
        _northWall = Wall.Create(_doc, Line.CreateBound(new XYZ(RoomWidth, RoomDepth, 0), new XYZ(0, RoomDepth, 0)), level.Id, false);
        _westWall = Wall.Create(_doc, Line.CreateBound(new XYZ(0, RoomDepth, 0), new XYZ(0, 0, 0)), level.Id, false);
        _farNorthWall = Wall.Create(_doc, Line.CreateBound(new XYZ(0, 30, 0), new XYZ(RoomWidth, 30, 0)), level.Id, false);
        _doc.Regenerate();
        _room = _doc.Create.NewRoom(level, new UV(CentreX, CentreY));

        // A view template made from an elevation view. In Revit it controls the far-clip mode.
        var elevationType = new FilteredElementCollector(_doc)
            .OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
            .First(t => t.ViewFamily == ViewFamily.Elevation);
        var marker = ElevationMarker.CreateElevationMarker(_doc, elevationType.Id, new XYZ(100, 100, 0), 48);
        var sourceView = marker.CreateElevation(_doc, _hostPlan.Id, 0);
        _elevationTemplate = sourceView.CreateViewTemplate();
        _elevationTemplate.Name = "Test Elevation Template";
        _doc.Delete(marker.Id);

        tx.Commit();
    }

    [After(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Cleanup()
    {
        _doc?.Close(false);
    }

    [Test]
    [Arguments(0.0, 1.0, "north")]
    [Arguments(1.0, 0.0, "east")]
    [Arguments(0.0, -1.0, "south")]
    [Arguments(-1.0, 0.0, "west")]
    public async Task Create_SideDirection_SeesIntendedWallInFrontAndNotTheOppositeWall(double dx, double dy, string side)
    {
        var (facing, opposite) = side switch
        {
            "north" => (_northWall, _southWall),
            "east" => (_eastWall, _westWall),
            "south" => (_southWall, _northWall),
            _ => (_westWall, _eastWall),
        };
        var acrossWidth = dx == 0 ? RoomWidth : RoomDepth;
        var depthToWall = dx == 0 ? RoomDepth / 2 : RoomWidth / 2;

        var result = CreateOne(Request($"Facing {side}", dx, dy, acrossWidth, depthToWall + 1.0));

        await Assert.That(result.Error).IsNull();
        var visibleWalls = VisibleWallIds(result.ViewId!.Value);
        await Assert.That(visibleWalls).Contains(facing.Id);
        await Assert.That(visibleWalls).DoesNotContain(opposite.Id);
    }

    [Test]
    public async Task Create_FarClip_ClipsWithoutLineAtGivenDistance()
    {
        var result = CreateOne(Request("Far clip", 0, 1, RoomWidth, 6.5));

        await Assert.That(result.Error).IsNull();
        var view = View(result.ViewId!.Value);
        await Assert.That(view.get_Parameter(BuiltInParameter.VIEWER_BOUND_FAR_CLIPPING).AsInteger()).IsEqualTo(2);
        await Assert.That(view.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR).AsDouble()).IsEqualTo(6.5).Within(1e-6);
        await Assert.That(VisibleWallIds(result.ViewId.Value)).DoesNotContain(_farNorthWall.Id);
    }

    [Test]
    [Arguments(0.0)]
    [Arguments(3.5)]
    public async Task Create_Crop_IsActiveRectangleCentredOnOriginFromFloorUp(double originZ)
    {
        var request = Request($"Crop at {originZ}", 0, 1, RoomWidth, 6);
        request.Origin = new[] { CentreX, CentreY, originZ };

        var result = CreateOne(request);

        await Assert.That(result.Error).IsNull();
        var view = View(result.ViewId!.Value);
        await Assert.That(view.CropBoxActive).IsTrue();
        var corners = view.GetCropRegionShapeManager().GetCropShape().Single().Select(c => c.GetEndPoint(0)).ToList();
        var expected = new[]
        {
            new XYZ(0, 5, originZ), new XYZ(20, 5, originZ),
            new XYZ(20, 5, originZ + CropHeight), new XYZ(0, 5, originZ + CropHeight),
        };
        await Assert.That(corners.Count).IsEqualTo(4);
        foreach (var corner in expected)
        {
            await Assert.That(corners.Any(c => c.IsAlmostEqualTo(corner, 1e-6))).IsTrue();
        }
    }

    [Test]
    public async Task Create_MixedBatch_CreatesGoodItemsAndLeavesNothingFromBadOnes()
    {
        var markersBefore = MarkerCount();
        var viewsBefore = ElevationViewCount();
        var zeroDirection = Request("Mixed zero direction", 0, 0, RoomWidth, 6);
        var badHost = Request("Mixed bad host", 0, 1, RoomWidth, 6);
        badHost.HostPlanId = _room.Id.Value;

        var results = ElevationViewFactory.Create(_doc, new[]
        {
            Request("Mixed good 1", 0, 1, RoomWidth, 6),
            Request("Mixed bad {name}", 0, 1, RoomWidth, 6), // fails inside the transaction
            zeroDirection,
            badHost,
            Request("Mixed good 2", 1, 0, RoomDepth, 11),
        }, new ElevationViewOptions());

        await Assert.That(results.Select(r => r.Index)).IsEquivalentTo(new[] { 0, 1, 2, 3, 4 });
        foreach (var bad in new[] { results[1], results[2], results[3] })
        {
            await Assert.That(bad.Error).IsNotNull();
            await Assert.That(bad.ViewId).IsNull();
            await Assert.That(bad.FinalName).IsNull();
        }
        await Assert.That(results[0].Error).IsNull();
        await Assert.That(results[4].Error).IsNull();
        await Assert.That(MarkerCount()).IsEqualTo(markersBefore + 2);
        await Assert.That(ElevationViewCount()).IsEqualTo(viewsBefore + 2);
    }

    [Test]
    public async Task Create_DuplicateNames_AddNumberedSuffixWithinBatchAndAcrossCalls()
    {
        var batch = new[] { Request("Duplicate", 0, 1, RoomWidth, 6), Request("Duplicate", 1, 0, RoomDepth, 11) };

        var first = ElevationViewFactory.Create(_doc, batch, new ElevationViewOptions());
        var repeat = ElevationViewFactory.Create(_doc, batch, new ElevationViewOptions());

        await Assert.That(first.Select(r => r.FinalName)).IsEquivalentTo(new[] { "Duplicate", "Duplicate (2)" });
        await Assert.That(repeat.Select(r => r.FinalName)).IsEquivalentTo(new[] { "Duplicate (3)", "Duplicate (4)" });
        await Assert.That(View(first[1].ViewId!.Value).Name).IsEqualTo("Duplicate (2)");
    }

    [Test]
    public async Task Create_UnknownViewTypeOrTemplate_FailsWholeCallListingValidNamesAndCreatesNothing()
    {
        var markersBefore = MarkerCount();
        var batch = new[] { Request("Never created", 0, 1, RoomWidth, 6) };

        var typeError = Assert.Throws<ArgumentException>(() =>
            ElevationViewFactory.Create(_doc, batch, new ElevationViewOptions { ViewTypeName = "No Such Type" }));
        var templateError = Assert.Throws<ArgumentException>(() =>
            ElevationViewFactory.Create(_doc, batch, new ElevationViewOptions { ViewTemplateName = "No Such Template" }));

        await Assert.That(typeError.Message).Contains("Elevation 1");
        await Assert.That(templateError.Message).Contains("Test Elevation Template");
        await Assert.That(MarkerCount()).IsEqualTo(markersBefore);
    }

    [Test]
    public async Task Create_TemplateControlsFarClip_KeepsTemplateSettingWithWarning()
    {
        var result = ElevationViewFactory.Create(_doc,
            new[] { Request("With template", 0, 1, RoomWidth, 6) },
            new ElevationViewOptions { ViewTemplateName = _elevationTemplate.Name })[0];

        await Assert.That(result.Error).IsNull();
        await Assert.That(View(result.ViewId!.Value).ViewTemplateId).IsEqualTo(_elevationTemplate.Id);
        await Assert.That(result.Warnings.Any(w => w.Contains("far clip", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task Create_WithRoomId_CopiesRoomPhase()
    {
        var request = Request("With room", 0, 1, RoomWidth, 6);
        request.RoomId = _room.Id.Value;

        var result = CreateOne(request);

        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Warnings).IsEmpty();
        await Assert.That(View(result.ViewId!.Value).get_Parameter(BuiltInParameter.VIEW_PHASE).AsElementId())
            .IsEqualTo(_room.get_Parameter(BuiltInParameter.ROOM_PHASE).AsElementId());
    }

    [Test]
    public async Task Create_RoomIdNotARoom_IsAWarningNotAnError()
    {
        var request = Request("Not a room", 0, 1, RoomWidth, 6);
        request.RoomId = _northWall.Id.Value;

        var result = CreateOne(request);

        await Assert.That(result.Error).IsNull();
        await Assert.That(result.ViewId).IsNotNull();
        await Assert.That(result.Warnings.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Create_TwelveViewBatch_Timing()
    {
        var directions = new[] { (0.0, 1.0), (1.0, 0.0), (0.0, -1.0), (-1.0, 0.0) };
        var batch = Enumerable.Range(0, 12)
            .Select(i => Request($"Timing {i}", directions[i % 4].Item1, directions[i % 4].Item2, RoomWidth, 11))
            .ToArray();

        var stopwatch = Stopwatch.StartNew();
        var results = ElevationViewFactory.Create(_doc, batch, new ElevationViewOptions());
        stopwatch.Stop();

        Console.WriteLine($"12-view batch took {stopwatch.ElapsedMilliseconds} ms");
        await Assert.That(results.All(r => r.Error == null)).IsTrue();
    }

    private static ElevationViewRequest Request(string name, double dx, double dy, double cropWidth, double farClip) => new()
    {
        HostPlanId = _hostPlan.Id.Value,
        Origin = new[] { CentreX, CentreY, 0.0 },
        ViewDirection = new[] { dx, dy },
        CropWidth = cropWidth,
        CropHeight = CropHeight,
        FarClip = farClip,
        Name = name,
    };

    private static ElevationViewResult CreateOne(ElevationViewRequest request) =>
        ElevationViewFactory.Create(_doc, new[] { request }, new ElevationViewOptions())[0];

    private static View View(long id) => (View)_doc.GetElement(new ElementId(id));

    private static List<ElementId> VisibleWallIds(long viewId) =>
        new FilteredElementCollector(_doc, new ElementId(viewId)).OfClass(typeof(Wall)).ToElementIds().ToList();

    // Not generic on purpose: a Revit-type generic constraint breaks the test runtime's start-up.
    private static int MarkerCount() =>
        new FilteredElementCollector(_doc).OfClass(typeof(ElevationMarker)).GetElementCount();

    private static int ElevationViewCount() =>
        new FilteredElementCollector(_doc).OfClass(typeof(ViewSection)).Cast<View>()
            .Count(v => !v.IsTemplate && v.ViewType == ViewType.Elevation);
}
