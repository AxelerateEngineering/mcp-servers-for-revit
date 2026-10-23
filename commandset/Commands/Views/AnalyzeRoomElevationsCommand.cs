using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Models.Views;
using RevitMCPCommandSet.Services.Views;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Views
{
    /// <summary>
    /// analyze_room_elevations: read-only analysis of the Interior Elevations each Room should get.
    /// </summary>
    public class AnalyzeRoomElevationsCommand : ExternalEventCommandBase
    {
        private const int TimeoutMilliseconds = 60000;

        private static readonly ElevationSide[] AllSides =
            { ElevationSide.North, ElevationSide.East, ElevationSide.South, ElevationSide.West };

        private AnalyzeRoomElevationsEventHandler _handler => (AnalyzeRoomElevationsEventHandler)Handler;

        public override string CommandName => "analyze_room_elevations";

        public AnalyzeRoomElevationsCommand(UIApplication uiApp)
            : base(new AnalyzeRoomElevationsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            var roomIds = ParseRoomIds(parameters?["roomIds"]);
            var sides = ParseSides(parameters?["sides"]);

            _handler.SetParameters(roomIds, sides);
            if (!RaiseAndWaitForCompletion(TimeoutMilliseconds))
            {
                throw new TimeoutException(
                    $"analyze_room_elevations timed out after {TimeoutMilliseconds / 1000} s; the outcome is unknown");
            }

            if (_handler.Error != null)
            {
                throw new InvalidOperationException(
                    $"analyze_room_elevations failed: {_handler.Error.Message}", _handler.Error);
            }

            return _handler.Result;
        }

        private static List<long> ParseRoomIds(JToken token)
        {
            if (!(token is JArray array) || array.Count == 0)
            {
                throw new ArgumentException("roomIds is required and must be a non-empty array of integers");
            }

            return array.Select(item =>
            {
                if (item.Type != JTokenType.Integer)
                {
                    throw new ArgumentException($"roomIds must contain integers, got '{item}'");
                }

                try
                {
                    return item.Value<long>();
                }
                catch (OverflowException)
                {
                    throw new ArgumentException($"Room id {item} is out of range");
                }
            }).ToList();
        }

        private static IReadOnlyCollection<ElevationSide> ParseSides(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return AllSides;
            }

            if (!(token is JArray array))
            {
                throw new ArgumentException("sides must be an array of \"North\", \"East\", \"South\" or \"West\"");
            }

            if (array.Count == 0)
            {
                return AllSides;
            }

            return array.Select(ParseSide).Distinct().ToList();
        }

        private static ElevationSide ParseSide(JToken item)
        {
            var name = item.Type == JTokenType.String ? item.Value<string>() : null;
            foreach (var side in AllSides)
            {
                if (string.Equals(side.ToString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    return side;
                }
            }

            throw new ArgumentException($"Unknown side '{item}'; use \"North\", \"East\", \"South\" or \"West\"");
        }
    }
}
