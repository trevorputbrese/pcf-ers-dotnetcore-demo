using Newtonsoft.Json;

namespace Nuke.Common.Tools.CloudFoundry;

public class Resource
{
    public string Guid { get; set; }

    [JsonProperty("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonProperty("updated_at")]
    public DateTime UpdatedAt { get; set; }

    public Dictionary<string, Link> Links { get; set; }
}