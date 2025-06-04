using Newtonsoft.Json;

namespace TwitchShoutout.Server.Dtos;


public class ChannelResponse
{
    [JsonProperty("data")] public List<ChannelData> Data { get; set; } = [];
}

public class ChannelData
{
    [JsonProperty("broadcaster_id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("broadcaster_login")] public string BroadCasterLogin { get; set; } = string.Empty;
    [JsonProperty("broadcaster_name")] public string BroadcasterName { get; set; } = string.Empty;
}