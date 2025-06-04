using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace TwitchShoutout.Database.Models;

[PrimaryKey(nameof(Id))]
[Index(nameof(Name), IsUnique = true)]
public class Pronoun
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [MaxLength(50)]
    [JsonProperty("id")] public int Id { get; set; }
    [JsonProperty("name")] public string Name { get; set; } = string.Empty;
    [JsonProperty("subject")] public string Subject { get; set; } = string.Empty;
    [JsonProperty("object")] public string Object { get; set; } = string.Empty;
    [JsonProperty("singular")] public bool Singular { get; set; } = false;
}