using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Models.Views;
using RevitMCPCommandSet.Services.Views;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Views
{
    public class CreateElevationViewsCommand : ExternalEventCommandBase
    {
        public const int MaxViews = 12;

        private CreateElevationViewsEventHandler _handler => (CreateElevationViewsEventHandler)Handler;

        public override string CommandName => "create_elevation_views";

        public CreateElevationViewsCommand(UIApplication uiApp)
            : base(new CreateElevationViewsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            if (!(parameters?["views"] is JArray items) || items.Count < 1 || items.Count > MaxViews)
                throw new ArgumentException($"views must be an array of 1 to {MaxViews} items.");

            var requests = items.Select(ParseItem).ToList();
            var options = new ElevationViewOptions
            {
                ViewTypeName = parameters["viewTypeName"]?.Value<string>(),
                ViewTemplateName = parameters["viewTemplateName"]?.Value<string>(),
                Scale = ParseScale(parameters["scale"]),
            };

            _handler.SetParameters(requests, options);
            if (!RaiseAndWaitForCompletion(CommandDeadlines.CreateElevationViewsMilliseconds))
                throw CommandDeadlines.Timeout(CommandName, CommandDeadlines.CreateElevationViewsMilliseconds);
            if (_handler.ErrorMessage != null)
                throw new InvalidOperationException(_handler.ErrorMessage);
            return _handler.Results;
        }

        /// <summary>An unreadable item becomes a request that fails validation, so the other items still run.</summary>
        private static ElevationViewRequest ParseItem(JToken item)
        {
            try
            {
                return item.ToObject<ElevationViewRequest>()
                       ?? new ElevationViewRequest { InputError = "The item is empty." };
            }
            catch (Exception ex)
            {
                return new ElevationViewRequest { InputError = $"The item could not be read: {ex.Message}" };
            }
        }

        private static int ParseScale(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return ElevationViewOptions.DefaultScale;
            if (token.Type != JTokenType.Integer || token.Value<long>() > int.MaxValue || token.Value<long>() < int.MinValue)
                throw new ArgumentException(
                    $"scale must be an integer from {ElevationViewFactory.MinScale} to {ElevationViewFactory.MaxScale}.");
            return token.Value<int>();
        }
    }
}
