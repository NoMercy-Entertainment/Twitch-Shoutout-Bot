using AspNet.Security.OAuth.Twitch;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using RestSharp;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using TwitchShoutout.Database.Models;
using TwitchShoutout.Database;
using TwitchShoutout.Server.Api.Constraints;
using TwitchShoutout.Server.Api.Swagger;
using TwitchShoutout.Server.Dtos;
using TwitchShoutout.Server.Helpers;
using TwitchShoutout.Server.Services;

namespace TwitchShoutout.Server.Config;

public static class ServiceConfiguration
{
    public static void ConfigureServices(WebApplicationBuilder builder)
    {
        ConfigureKestrel(builder);
        ConfigureCoreServices(builder);
        ConfigureLogging(builder);
        ConfigureAuth(builder);
        ConfigureApi(builder);
        ConfigureCors(builder);
    }
    
    private static void ConfigureKestrel(WebApplicationBuilder builder)
    {
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = null;
            options.Limits.MaxRequestBufferSize = null;
            options.Limits.MaxConcurrentConnections = null;
            options.Limits.MaxConcurrentUpgradedConnections = null;
            options.ListenAnyIP(5001);
        });
    }
    
    private static void ConfigureCoreServices(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<TokenStore>();
        builder.Services.AddSingleton<BotDbContext>();
        builder.Services.AddSingleton<PronounService>();
        builder.Services.AddSingleton<TwitchApiService>();
        builder.Services.AddSingleton<TwitchAuthService>();
        builder.Services.AddHostedService<Worker>();
        builder.Services.AddSingleton<Worker>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddDirectoryBrowser();
        builder.Services.AddResponseCompression(options => options.EnableForHttps = true);
    }

    private static void ConfigureLogging(WebApplicationBuilder builder)
    {
        builder.Services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.AddFilter("Microsoft", LogLevel.Warning);
            logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
            logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
            logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);
            logging.AddFilter("Microsoft.AspNetCore.HttpsPolicy.HttpsRedirectionMiddleware", LogLevel.Error);
            logging.SetMinimumLevel(LogLevel.Debug);
        });
    }

    private static void ConfigureAuth(WebApplicationBuilder builder)
    {
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy("api", ConfigureAuthPolicy());

        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = "BearerToken";
            options.DefaultChallengeScheme = "BearerToken";
            options.DefaultSignInScheme = "BearerToken";
        })
        .AddBearerToken(ConfigureBearerToken)
        .AddTwitch(ConfigureTwitchAuth);
    }

    private static void ConfigureApi(WebApplicationBuilder builder)
    {
        builder.Services.AddControllers(options =>
            {
                options.EnableEndpointRouting = true; // This is the default, but explicit for clarity
            })
            .AddNewtonsoftJson(options => 
                options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore)
            .AddJsonOptions(options => 
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        builder.Services.Configure<RouteOptions>(options =>
            options.ConstraintMap.Add("ulid", typeof(UlidRouteConstraint)));

        ConfigureApiVersioning(builder);
        ConfigureSwagger(builder);
    }

    private static void ConfigureCors(WebApplicationBuilder builder)
    {
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowOrigins", configurePolicy =>
            {
                configurePolicy
                    .WithOrigins(
                        "https://nomercy.tv",
                        "https://*.nomercy.tv",
                        "http://localhost:5001",
                        "https://localhost")
                    .AllowAnyMethod()
                    .AllowCredentials()
                    .SetIsOriginAllowedToAllowWildcardSubdomains()
                    .WithHeaders("Access-Control-Allow-Private-Network", "true")
                    .WithHeaders("Access-Control-Allow-Headers", "*")
                    .AllowAnyHeader();
            });
        });
    }
    private static void ConfigureApiVersioning(WebApplicationBuilder builder)
    {
        builder.Services.AddApiVersioning(config =>
            {
                config.ReportApiVersions = true;
                config.AssumeDefaultVersionWhenUnspecified = true;
                config.DefaultApiVersion = new(1, 0);
                config.UnsupportedApiVersionStatusCode = 418;
            })
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
                options.DefaultApiVersion = new(1, 0);
            });
    }
    
    private static void ConfigureSwagger(WebApplicationBuilder builder)
    {
        builder.Services.AddSwaggerGen(options => options.OperationFilter<SwaggerDefaultValues>());
        builder.Services.AddSwaggerGenNewtonsoftSupport();
        builder.Services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();
    }
    
    private static Action<AuthorizationPolicyBuilder> ConfigureAuthPolicy()
    {
        return policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.AddAuthenticationSchemes(IdentityConstants.BearerScheme);
            policy.RequireClaim("scope", "user:read:email");
            policy.AddRequirements(new AssertionRequirement(context =>
            {
                TwitchUser? user = ClaimsPrincipleExtensions.Users
                    .FirstOrDefault(user => user.Id == context.User.FindFirstValue(ClaimTypes.NameIdentifier));
                        
                return user is not null;
            }));
        };
    }
    
    private static Action<BearerTokenOptions> ConfigureBearerToken
    {
        get => options =>
        {
            options.Events.OnMessageReceived = new(async message =>
            {
                string[] result = message.Request.Query["access_token"].ToString().Split('&');
    
                if (result.Length > 0 && !string.IsNullOrEmpty(result[0]))
                {
                    message.Request.Headers.Authorization = $"Bearer {result[0]}";
                }
                        
                if (!message.Request.Headers.TryGetValue("Authorization", out StringValues authHeader))
                {
                    message.Fail("No authorization header");
                    await Task.CompletedTask;
                    return;
                }
    
                string? accessToken = authHeader.ToString().Split("Bearer ").LastOrDefault();
                if (string.IsNullOrEmpty(accessToken))
                {
                    message.Fail("No token provided");
                    await Task.CompletedTask;
                    return;
                }
    
                RestClient client = new($"{Globals.TwitchAuthUrl}/validate");
                RestRequest request = new();
                request.AddHeader("Authorization", $"OAuth {accessToken}");
    
                RestResponse response = await client.ExecuteAsync(request);
                if (!response.IsSuccessful)
                {
                    message.Fail("Failed to validate token");
                    await Task.CompletedTask;
                    return;
                }
    
                ValidatedTwitchAuthResponse? user = response.Content?.FromJson<ValidatedTwitchAuthResponse>();
                if (user?.UserId is null)
                {
                    message.Fail("Invalid token");
                    await Task.CompletedTask;
                    return;
                }
    
                message.HttpContext.User = new(new ClaimsIdentity([
                    new(ClaimTypes.NameIdentifier, user.UserId),
                    new(ClaimTypes.Name, user.Login)
                ], "BearerToken"));
    
                await Task.CompletedTask;
            });
        };
    }
    
    private static Action<TwitchAuthenticationOptions> ConfigureTwitchAuth
    {
        get => options =>
        {
            options.ClientId = Globals.ClientId;
            options.ClientSecret = Globals.ClientSecret;
            options.UsePkce = true;
            options.SaveTokens = true;
            options.Scope.Add("user:read:email");
        };
    }
    
}