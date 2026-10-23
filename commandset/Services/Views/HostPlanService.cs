using RevitMCPCommandSet.Models.Views;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services.Views
{
    /// <summary>
    /// Makes sure each level has a Host Plan: reuses the first floor plan on the level,
    /// or creates "{Level name} - Elevation Host".
    /// </summary>
    public static class HostPlanService
    {
        public const string HostPlanNameSuffix = " - Elevation Host";

        public static List<HostPlanResult> Ensure(Document doc, IEnumerable<long> levelIds)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (levelIds == null) throw new ArgumentNullException(nameof(levelIds));

            var names = new ViewNameRegistry(doc);
            return levelIds.Select(levelId => EnsureOne(doc, levelId, names)).ToList();
        }

        private static HostPlanResult EnsureOne(Document doc, long levelId, ViewNameRegistry names)
        {
            var result = new HostPlanResult { LevelId = levelId };

            if (!RequestIds.IsValid(levelId))
            {
                result.Error = $"Level id {levelId} is not a positive safe integer.";
                return result;
            }

            if (!(RequestIds.GetElement(doc, levelId) is Level level))
            {
                result.Error = $"Element {levelId} is not a level in the active document.";
                return result;
            }

            var existing = FindFloorPlan(doc, level.Id);
            if (existing != null)
            {
                result.PlanId = existing.Id.GetValue();
                result.PlanName = existing.Name;
                return result;
            }

            var floorPlanType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == ViewFamily.FloorPlan);
            if (floorPlanType == null)
            {
                result.Error = "The document has no floor plan view type.";
                return result;
            }

            var name = names.Propose(level.Name + HostPlanNameSuffix);
            ViewPlan plan = null;
            var error = ElevationFailurePreprocessor.RunTransaction(doc, "Create Host Plan", () =>
            {
                plan = ViewPlan.Create(doc, floorPlanType.Id, level.Id);
                plan.Name = name;
            }, new List<string>());

            if (error != null)
            {
                result.Error = error;
                return result;
            }

            names.Reserve(name);
            result.PlanId = plan.Id.GetValue();
            result.PlanName = plan.Name;
            result.Created = true;
            return result;
        }

        private static ViewPlan FindFloorPlan(Document doc, ElementId levelId) =>
            new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .FirstOrDefault(v => v.ViewType == ViewType.FloorPlan
                                     && !v.IsTemplate
                                     && v.GenLevel != null
                                     && v.GenLevel.Id == levelId);
    }
}
