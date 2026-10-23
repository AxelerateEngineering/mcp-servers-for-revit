using Newtonsoft.Json;

namespace RevitMCPCommandSet.Models.Views
{
    /// <summary>
    /// One Interior Elevation to create. All values are in internal model coordinates, in feet,
    /// normally copied unchanged from one side of analyze_room_elevations.
    /// </summary>
    public class ElevationViewRequest
    {
        [JsonProperty("hostPlanId")]
        public long HostPlanId { get; set; }

        /// <summary>[x, y, z]: the point the view looks from; z is the Room's floor.</summary>
        [JsonProperty("origin")]
        public double[] Origin { get; set; }

        /// <summary>[dx, dy]: the horizontal direction the view looks in.</summary>
        [JsonProperty("viewDirection")]
        public double[] ViewDirection { get; set; }

        [JsonProperty("cropWidth")]
        public double CropWidth { get; set; }

        [JsonProperty("cropHeight")]
        public double CropHeight { get; set; }

        [JsonProperty("farClip")]
        public double FarClip { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("roomId")]
        public long? RoomId { get; set; }

        /// <summary>Set by the command when the JSON item could not be read; the item then fails validation.</summary>
        [JsonIgnore]
        public string InputError { get; set; }
    }

    /// <summary>Options that apply to every view in one create_elevation_views call.</summary>
    public class ElevationViewOptions
    {
        public const int DefaultScale = 48;

        /// <summary>Elevation ViewFamilyType name; null means the first Elevation type.</summary>
        public string ViewTypeName { get; set; }

        /// <summary>View template name; null means no template.</summary>
        public string ViewTemplateName { get; set; }

        public int Scale { get; set; } = DefaultScale;
    }

    /// <summary>Result for one requested view, in input order.</summary>
    public class ElevationViewResult
    {
        [JsonProperty("index")]
        public int Index { get; set; }

        [JsonProperty("viewId")]
        public long? ViewId { get; set; }

        [JsonProperty("finalName")]
        public string FinalName { get; set; }

        [JsonProperty("warnings")]
        public List<string> Warnings { get; set; } = new List<string>();

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public string Error { get; set; }
    }
}
