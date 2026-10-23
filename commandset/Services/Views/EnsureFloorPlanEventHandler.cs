using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Views;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Views
{
    /// <summary>
    /// Thin wrapper that runs <see cref="HostPlanService"/> on the Revit thread.
    /// The completion signal is reset in SetParameters (before Raise) and only waited on in
    /// WaitForCompletion, so a fast Execute can never be missed.
    /// </summary>
    public class EnsureFloorPlanEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _completed = new ManualResetEvent(false);
        private IReadOnlyList<long> _levelIds;

        public List<HostPlanResult> Results { get; private set; }

        public string ErrorMessage { get; private set; }

        public void SetParameters(IReadOnlyList<long> levelIds)
        {
            _levelIds = levelIds;
            Results = null;
            ErrorMessage = null;
            _completed.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds) => _completed.WaitOne(timeoutMilliseconds);

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document
                          ?? throw new InvalidOperationException("There is no active Revit document.");
                Results = HostPlanService.Ensure(doc, _levelIds);
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                _completed.Set();
            }
        }

        public string GetName() => "Ensure Host Plans";
    }
}
