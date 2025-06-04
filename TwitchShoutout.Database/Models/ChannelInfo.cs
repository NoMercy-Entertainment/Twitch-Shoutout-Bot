using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace TwitchShoutout.Database.Models;

[PrimaryKey(nameof(Id))]
public class ChannelInfo
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [MaxLength(50)]
    [JsonProperty("broadcaster_id")] public string Id { get; set; } = string.Empty;
    
    [MaxLength(50)]
    [JsonProperty("broadcaster_language")] public string Language { get; set; } = string.Empty;
    
    [MaxLength(50)]
    [JsonProperty("game_id")] public string GameId { get; set; } = string.Empty;
    
    [MaxLength(255)]
    [JsonProperty("game_name")] public string GameName { get; set; } = string.Empty;
    
    [MaxLength(255)]
    [JsonProperty("title")] public string Title { get; set; } = string.Empty;
    
    [JsonProperty("delay")] public int Delay { get; set; }
    
    [JsonProperty("tags")] public string TagsJson { get; set; } = "[]";
    
    [NotMapped]
    public List<string> Tags
    {
        get => JsonConvert.DeserializeObject<List<string>>(TagsJson) ?? [];
        set => TagsJson = JsonConvert.SerializeObject(value);
    }
    
    [JsonProperty("content_classification_labels")] public string LabelsJson { get; set; } = "[]";
    
    [NotMapped]
    public List<string> ContentLabels
    {
        get => JsonConvert.DeserializeObject<List<string>>(LabelsJson) ?? [];
        set => LabelsJson = JsonConvert.SerializeObject(value);
    }
    
    [JsonProperty("is_branded_content")] public bool IsBrandedContent { get; set; }
    
    [ForeignKey(nameof(Id))]
    public virtual Channel Channel { get; set; } = null!;
}