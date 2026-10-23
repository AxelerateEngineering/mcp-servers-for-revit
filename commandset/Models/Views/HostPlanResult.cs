using Newtonsoft.Json;

namespace RevitMCPCommandSet.Models.Views
{
    /// <summary>
    /// Result of ensure_floor_plan for one level: the Host Plan used for that level.
    /// </summary>
    public class HostPlanResult
    {
        [JsonProperty("levelId")]
        public long LevelId { get; set; }

        [JsonProperty("planId")]
        public long? PlanId { get; set; }

        [JsonProperty("planName")]
        public string PlanName { get; set; }

        [JsonProperty("created")]
        public bool Created { get; set; }

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public string Error { get; set; }
    }
}
