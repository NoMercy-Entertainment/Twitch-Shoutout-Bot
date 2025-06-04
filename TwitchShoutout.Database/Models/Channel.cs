using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace TwitchShoutout.Database.Models;

[PrimaryKey(nameof(Id))]
public class Channel
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [MaxLength(50)]
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    
    [MaxLength(25)]
    [JsonProperty("name")] public string Name { get; set; } = null!;
    [JsonProperty("enabled")] public bool Enabled { get; set; } = true;

    [ForeignKey(nameof(Id))]
    [JsonProperty("broadcaster")] public virtual TwitchUser User { get; set; } = null!;
    
    [JsonProperty("shoutouts")] public ICollection<Shoutout> Shoutouts { get; set; } = [];
    
    [JsonProperty("moderated_for")] public ICollection<ChannelModerators> ChannelModerators { get; set; } = new List<ChannelModerators>();

}

public sealed class SimpleChannel
{
    [JsonProperty("id")] public string Id { get; set; } 
    [JsonProperty("name")] public string Name { get; set; }
    [JsonProperty("enabled")] public bool Enabled { get; set; }
    [JsonProperty("broadcaster")] public new SimpleUser User { get; set; }
    [JsonProperty("shoutouts")] public new IEnumerable<SimpleShoutout> Shoutouts { get; set; }
    [JsonProperty("moderated_for")] public new IEnumerable<SimpleChannelModerator> ChannelModerators { get; set; }
    
    public SimpleChannel(Channel channel)
    {
        Id = channel.Id;
        Name = channel.Name;
        Enabled = channel.Enabled;
        User = new(channel.User);
        Shoutouts = channel.Shoutouts.Select(s => new SimpleShoutout(s));
        ChannelModerators = channel.ChannelModerators.Select(m => new SimpleChannelModerator(m.User, m.Channel));
    }
}