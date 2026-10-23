using Autodesk.Revit.DB;
using Nice3point.TUnit.Revit;
using Nice3point.TUnit.Revit.Executors;
using RevitMCPCommandSet.Services.Views;
using TUnit.Core;
using TUnit.Core.Executors;

namespace RevitMCPCommandSet.Tests.Views;

public class HostPlanServiceTests : RevitApiTest
{
    private static Document _doc = null!;
    private static Level _levelWithPlan = null!;
    private static Level _levelWithoutPlan = null!;
    private static ViewPlan _existingPlan = null!;

    [Before(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Setup()
    {
        _doc = Application.NewProjectDocument(UnitSystem.Imperial);

        using var tx = new Transaction(_doc, "Setup Host Plan Tests");
        tx.Start();

        _levelWithPlan = Level.Create(_doc, 0.0);
        _levelWithPlan.Name = "Host Plan Level A";
        _existingPlan = ViewPlan.Create(_doc, FloorPlanTypeId(_doc), _levelWithPlan.Id);

        _levelWithoutPlan = Level.Create(_doc, 12.0);
        _levelWithoutPlan.Name = "Host Plan Level B";

        tx.Commit();
    }

    [After(HookType.Class)]
    [HookExecutor<RevitThreadExecutor>]
    public static void Cleanup()
    {
        _doc?.Close(false);
    }

    [Test]
    public async Task Ensure_LevelWithFloorPlan_ReturnsExistingPlan()
    {
        var results = HostPlanService.Ensure(_doc, new[] { _levelWithPlan.Id.Value });

        await Assert.That(results.Count).IsEqualTo(1);
        var result = results[0];
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Created).IsFalse();
        await Assert.That(result.LevelId).IsEqualTo(_levelWithPlan.Id.Value);
        await Assert.That(result.PlanId).IsEqualTo(_existingPlan.Id.Value);
        await Assert.That(result.PlanName).IsEqualTo(_existingPlan.Name);
    }

    [Test]
    public async Task Ensure_LevelWithoutFloorPlan_CreatesNamedHostPlanOnThatLevel()
    {
        var results = HostPlanService.Ensure(_doc, new[] { _levelWithoutPlan.Id.Value });

        var result = results[0];
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Created).IsTrue();
        await Assert.That(result.PlanName).IsEqualTo("Host Plan Level B - Elevation Host");

        var plan = _doc.GetElement(new ElementId(result.PlanId!.Value)) as ViewPlan;
        await Assert.That(plan).IsNotNull();
        await Assert.That(plan!.ViewType).IsEqualTo(ViewType.FloorPlan);
        await Assert.That(plan.GenLevel.Id).IsEqualTo(_levelWithoutPlan.Id);

        var again = HostPlanService.Ensure(_doc, new[] { _levelWithoutPlan.Id.Value })[0];
        await Assert.That(again.Created).IsFalse();
        await Assert.That(again.PlanId).IsEqualTo(result.PlanId);
    }

    [Test]
    public async Task Ensure_HostPlanNameTaken_AddsSuffix()
    {
        Level level;
        using (var tx = new Transaction(_doc, "Add level with clashing view name"))
        {
            tx.Start();
            level = Level.Create(_doc, 24.0);
            level.Name = "Host Plan Level C";
            var clash = ViewPlan.Create(_doc, FloorPlanTypeId(_doc), _levelWithPlan.Id);
            clash.Name = "Host Plan Level C - Elevation Host";
            tx.Commit();
        }

        var result = HostPlanService.Ensure(_doc, new[] { level.Id.Value })[0];

        await Assert.That(result.Created).IsTrue();
        await Assert.That(result.PlanName).IsEqualTo("Host Plan Level C - Elevation Host (2)");
    }

    [Test]
    public async Task Ensure_BadIds_ReportErrorsWithoutStoppingOthers()
    {
        var results = HostPlanService.Ensure(_doc, new[] { -5L, 99999999L, _levelWithPlan.Id.Value });

        await Assert.That(results.Count).IsEqualTo(3);
        await Assert.That(results[0].Error).IsNotNull();
        await Assert.That(results[0].PlanId).IsNull();
        await Assert.That(results[1].Error).IsNotNull();
        await Assert.That(results[2].Error).IsNull();
        await Assert.That(results[2].PlanId).IsEqualTo(_existingPlan.Id.Value);
    }

    private static ElementId FloorPlanTypeId(Document doc) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .First(t => t.ViewFamily == ViewFamily.FloorPlan)
            .Id;
}
