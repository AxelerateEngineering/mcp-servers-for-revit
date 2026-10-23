using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.Views;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Views
{
    public class EnsureFloorPlanCommand : ExternalEventCommandBase
    {
        private EnsureFloorPlanEventHandler _handler => (EnsureFloorPlanEventHandler)Handler;

        public override string CommandName => "ensure_floor_plan";

        public EnsureFloorPlanCommand(UIApplication uiApp)
            : base(new EnsureFloorPlanEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            List<long> levelIds;
            try
            {
                levelIds = parameters?["levelIds"]?.ToObject<List<long>>();
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"levelIds must be an array of integer element ids: {ex.Message}");
            }
            if (levelIds == null || levelIds.Count == 0)
                throw new ArgumentException("levelIds must be a non-empty array of integer element ids.");

            _handler.SetParameters(levelIds);
            if (!RaiseAndWaitForCompletion(CommandDeadlines.EnsureFloorPlanMilliseconds))
                throw CommandDeadlines.Timeout(CommandName, CommandDeadlines.EnsureFloorPlanMilliseconds);
            if (_handler.ErrorMessage != null)
                throw new InvalidOperationException(_handler.ErrorMessage);
            return _handler.Results;
        }
    }
}
