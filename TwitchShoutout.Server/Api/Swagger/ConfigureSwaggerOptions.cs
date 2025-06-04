using Asp.Versioning.ApiExplorer;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace TwitchShoutout.Server.Api.Swagger;

public class ConfigureSwaggerOptions : IConfigureOptions<SwaggerGenOptions>
{
    private readonly IApiVersionDescriptionProvider _provider;

    public ConfigureSwaggerOptions(IApiVersionDescriptionProvider provider)
    {
        _provider = provider;
    }

    public void Configure(SwaggerGenOptions options)
    {
        foreach (ApiVersionDescription description in _provider.ApiVersionDescriptions)
        {
            options.SwaggerDoc(description.GroupName, CreateInfoForApiVersion(description));

            // options.AddServer(new()
            // {
            //     Url = "https://autoshoutbot.nomercy.tv/",
            //     Description = "Twitch Shoutout Bot API Server",
            // });

            options.AddSecurityDefinition("oauth2", new()
            {
                Type = SecuritySchemeType.OAuth2,
                Flows = new()
                {
                    Implicit = new()
                    {
                        AuthorizationUrl = new("https://id.twitch.tv/oauth2/authorize"),
                        Scopes = new Dictionary<string, string>
                        {
                            { "user:read:email", "Read user email" }
                        },
                    },
                },
            });

            OpenApiSecurityScheme oauth2SecurityScheme = new()
            {
                Reference = new()
                {
                    Id = "oauth2",
                    Type = ReferenceType.SecurityScheme
                },
                In = ParameterLocation.Path,
                Name = "Bearer",
                Scheme = "Bearer",
            };
            
            options.AddSecurityRequirement(new()
            {
                { oauth2SecurityScheme, [] },
                {
                    new() { Reference = new() { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
                    []
                }
            });
        }
    }

    private static OpenApiInfo CreateInfoForApiVersion(ApiVersionDescription description)
    {
        OpenApiInfo info = new()
        {
            Title = "Twitch Shoutout Bot API",
            Version = description.ApiVersion.ToString(),
            Description = "Twitch Shoutout Bot API",
            Contact = new()
            {
                Name = "NoMercy Entertainment",
                Email = "info@nomercy.tv",
                Url = new("https://nomercy.tv")
            },
            TermsOfService = new("https://nomercy.tv/terms-of-service"),
        };
        
        if (description.IsDeprecated) info.Description += " This API version has been deprecated.";

        return info;
    }
}