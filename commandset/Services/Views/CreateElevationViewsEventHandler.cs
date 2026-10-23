using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Views;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Views
{
    /// <summary>
    /// Thin wrapper that runs <see cref="ElevationViewFactory"/> on the Revit thread.
    /// The completion signal is reset in SetParameters (before Raise) and only waited on in
    /// WaitForCompletion, so a fast Execute can never be missed.
    /// </summary>
    public class CreateElevationViewsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _completed = new ManualResetEvent(false);
        private IReadOnlyList<ElevationViewRequest> _requests;
        private ElevationViewOptions _options;

        public List<ElevationViewResult> Results { get; private set; }

        /// <summary>Set when the whole call failed (e.g. unknown view type or template name).</summary>
        public string ErrorMessage { get; private set; }

        public void SetParameters(IReadOnlyList<ElevationViewRequest> requests, ElevationViewOptions options)
        {
            _requests = requests;
            _options = options;
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
                Results = ElevationViewFactory.Create(doc, _requests, _options);
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

        public string GetName() => "Create Interior Elevations";
    }
}
