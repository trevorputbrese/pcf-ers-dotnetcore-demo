using JetBrains.Annotations;
using Newtonsoft.Json;

namespace Nuke.Common.Tools.CloudFoundry;

public class Pagination
{
    [JsonProperty("total_results")]
    public int TotalResults { get; set; }
    [JsonProperty("total_pages")]
    public int TotalPages { get; set; }
    public Link First { get; set; }
    [CanBeNull] public Link Next { get; set; }
}