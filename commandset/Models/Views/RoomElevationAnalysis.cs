using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace RevitMCPCommandSet.Models.Views
{
    /// <summary>
    /// One of four plan directions around a Room: up, right, down and left in model coordinates.
    /// Not true north. Written to JSON as its name ("North", "East", "South" or "West").
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum ElevationSide
    {
        North,
        East,
        South,
        West
    }

    /// <summary>
    /// Result of analyze_room_elevations: one entry per requested room, in input order.
    /// </summary>
    public class RoomElevationAnalysisResult
    {
        [JsonProperty("rooms")]
        public List<RoomElevationAnalysis> Rooms { get; set; } = new List<RoomElevationAnalysis>();
    }

    /// <summary>
    /// The Interior Elevations a Room should get. Lengths and coordinates are in feet,
    /// in internal model coordinates.
    /// </summary>
    public class RoomElevationAnalysis
    {
        [JsonProperty("roomId")]
        public long RoomId { get; set; }

        [JsonProperty("number")]
        public string Number { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("levelId")]
        public long? LevelId { get; set; }

        /// <summary>First non-template floor plan on the room's level, or null when there is none.</summary>
        [JsonProperty("hostPlanId")]
        public long? HostPlanId { get; set; }

        [JsonProperty("sides")]
        public List<ElevationSideAnalysis> Sides { get; set; } = new List<ElevationSideAnalysis>();

        [JsonProperty("skippedSides")]
        public List<SkippedElevationSide> SkippedSides { get; set; } = new List<SkippedElevationSide>();

        [JsonProperty("warnings")]
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>Set when the room can't be analysed at all.</summary>
        [JsonProperty("error")]
        public string Error { get; set; }
    }

    /// <summary>
    /// The values for one Interior Elevation, ready to pass to create_elevation_views.
    /// </summary>
    public class ElevationSideAnalysis
    {
        [JsonProperty("side")]
        public ElevationSide Side { get; set; }

        /// <summary>[x, y, z]: the room's centroid in XY, at the room's floor.</summary>
        [JsonProperty("origin")]
        public double[] Origin { get; set; }

        /// <summary>[dx, dy]: horizontal unit vector the view looks along.</summary>
        [JsonProperty("viewDirection")]
        public double[] ViewDirection { get; set; }

        [JsonProperty("cropWidth")]
        public double CropWidth { get; set; }

        [JsonProperty("cropHeight")]
        public double CropHeight { get; set; }

        [JsonProperty("farClip")]
        public double FarClip { get; set; }

        [JsonProperty("suggestedName")]
        public string SuggestedName { get; set; }
    }

    public class SkippedElevationSide
    {
        [JsonProperty("side")]
        public ElevationSide Side { get; set; }

        [JsonProperty("reason")]
        public string Reason { get; set; }
    }
}
