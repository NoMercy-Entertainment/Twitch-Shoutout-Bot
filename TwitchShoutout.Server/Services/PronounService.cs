using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using RestSharp;
using TwitchShoutout.Database;
using TwitchShoutout.Database.Models;
using TwitchShoutout.Server.Dtos;

namespace TwitchShoutout.Server.Services;

public class PronounService
{
    private readonly RestClient _client;
    private readonly BotDbContext _dbContext;
    private readonly ILogger<PronounService> _logger;
    private static readonly Dictionary<string, Pronoun> Pronouns = new();

    public PronounService(BotDbContext dbContext, ILogger<PronounService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
        _client = new("https://api.pronouns.alejo.io/v1/");
    }

    public async Task LoadPronouns()
    {
        try
        {
            if (Pronouns.Any()) return;
            
            RestRequest request = new("pronouns");
            RestResponse response = await _client.ExecuteAsync(request);

            if (!response.IsSuccessful || response.Content == null)
                throw new("Failed to fetch pronouns");

            PronounResponse? pronounsResponse = JsonConvert.DeserializeObject<PronounResponse>(response.Content);
            if (pronounsResponse == null) return;

            foreach ((string key, Pronoun pronoun) in pronounsResponse)
            {
                Pronouns[key] = pronoun;

                await _dbContext.Pronouns.Upsert(pronoun)
                    .On(p => p.Name)
                    .WhenMatched((_, newPronoun) => new()
                    {
                        Name = newPronoun.Name,
                        Subject = newPronoun.Subject,
                        Object = newPronoun.Object,
                        Singular = newPronoun.Singular
                    })
                    .RunAsync();
            }

            _logger.LogInformation($"Loaded {Pronouns.Count} pronouns");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error loading pronouns: {ex.Message}");
        }
    }

    public async Task<Pronoun?> GetUserPronoun(string username)
    {
        try
        {
            RestRequest request = new($"users/{username}");
            RestResponse response = await _client.ExecuteAsync(request);

            if (!response.IsSuccessful || response.Content == null)
                return null;

            UserPronounResponse? userPronoun = JsonConvert.DeserializeObject<UserPronounResponse>(response.Content);
            if (userPronoun == null) return null;

            return await _dbContext.Pronouns.FirstOrDefaultAsync(p => p.Name == userPronoun.PronounId);
        }
        catch
        {
            return null;
        }
    }
}