using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Views;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Views
{
    /// <summary>
    /// Thin wrapper that runs <see cref="RoomElevationAnalyzer"/> on the Revit thread. Read-only: no transaction.
    /// The completion signal is reset in SetParameters (before Raise) and only waited on in
    /// WaitForCompletion, so a fast Execute can never be missed.
    /// </summary>
    public class AnalyzeRoomElevationsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _completed = new ManualResetEvent(false);
        private IReadOnlyList<long> _roomIds = Array.Empty<long>();
        private IReadOnlyCollection<ElevationSide> _sides = Array.Empty<ElevationSide>();

        public RoomElevationAnalysisResult Result { get; private set; }

        /// <summary>Set when the analysis as a whole failed; per-room problems go in each room's error.</summary>
        public string ErrorMessage { get; private set; }

        public void SetParameters(IReadOnlyList<long> roomIds, IReadOnlyCollection<ElevationSide> sides)
        {
            _roomIds = roomIds;
            _sides = sides;
            Result = null;
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
                Result = RoomElevationAnalyzer.Analyze(doc, _roomIds, _sides);
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

        public string GetName() => "Analyze Interior Elevations";
    }
}
