using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Views;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Views
{
    /// <summary>
    /// Thin wrapper that runs <see cref="RoomElevationAnalyzer"/> on the Revit thread. Read-only: no transaction.
    /// </summary>
    public class AnalyzeRoomElevationsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _completed = new ManualResetEvent(false);
        private IReadOnlyList<long> _roomIds = Array.Empty<long>();
        private IReadOnlyCollection<ElevationSide> _sides = Array.Empty<ElevationSide>();

        public RoomElevationAnalysisResult Result { get; private set; }

        /// <summary>Set when the analysis as a whole failed; per-room problems go in each room's error.</summary>
        public Exception Error { get; private set; }

        /// <summary>
        /// Stores the request and resets the completion signal. Must be called before the event is raised.
        /// </summary>
        public void SetParameters(IReadOnlyList<long> roomIds, IReadOnlyCollection<ElevationSide> sides)
        {
            _roomIds = roomIds;
            _sides = sides;
            Result = null;
            Error = null;
            _completed.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds)
        {
            return _completed.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document
                          ?? throw new InvalidOperationException("No active Revit document");
                Result = RoomElevationAnalyzer.Analyze(doc, _roomIds, _sides);
            }
            catch (Exception ex)
            {
                Error = ex;
            }
            finally
            {
                _completed.Set();
            }
        }

        public string GetName() => "Analyze Room Elevations";
    }
}
