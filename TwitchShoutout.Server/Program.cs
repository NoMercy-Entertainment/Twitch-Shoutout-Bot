using System.Security.Claims;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Text.Json.Serialization;
using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using AspNetCore.Swagger.Themes;
using DotNetEnv;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using RestSharp;
using TwitchShoutout.Database;
using TwitchShoutout.Database.Models;
using TwitchShoutout.Server;
using TwitchShoutout.Server.Api.Constraints;
using TwitchShoutout.Server.Api.Swagger;
using TwitchShoutout.Server.Config;
using TwitchShoutout.Server.Dtos;
using TwitchShoutout.Server.Helpers;
using TwitchShoutout.Server.Services;

Env.Load();

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<TokenStore>();
builder.Services.AddSingleton<BotDbContext>();

builder.Services.AddSingleton<TwitchApiService>();
builder.Services.AddSingleton<TwitchAuthService>();

builder.Services.Configure<IServiceCollection>(provider =>
{
    provider.AddSingleton<IApiVersionDescriptionProvider, DefaultApiVersionDescriptionProvider>();
    provider.AddSingleton<ISunsetPolicyManager, DefaultSunsetPolicyManager>();
});
builder.Services.Configure<ILoggingBuilder>(logging =>
{
    logging.ClearProviders();
    logging.AddFilter("Microsoft", LogLevel.None);
});

builder.Services.AddLogging(builder =>
{
    builder.AddFilter("Microsoft", LogLevel.Warning);
    builder.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
    builder.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
    builder.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);
    builder.AddFilter("Microsoft.AspNetCore.HttpsPolicy.HttpsRedirectionMiddleware", LogLevel.Error);
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = null;
    options.Limits.MaxRequestBufferSize = null;
    options.Limits.MaxConcurrentConnections = null;
    options.Limits.MaxConcurrentUpgradedConnections = null;
    options.ListenAnyIP(5001);
});

builder.Services.AddHostedService<Worker>();

builder.Services.AddControllers()
    .AddNewtonsoftJson(options =>
    {
        options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.Configure<RouteOptions>(options =>
{
    options.ConstraintMap.Add("ulid", typeof(UlidRouteConstraint));
});

// Configure Logging
builder.Services.AddLogging(config =>
{
    config.AddFilter("Microsoft.EntityFrameworkCore.TwitchShoutout.Database.Command", LogLevel.Warning);
});

// Configure Authorization
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("api", policy =>
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
    });


builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = "BearerToken";
    options.DefaultChallengeScheme = "BearerToken";
    options.DefaultSignInScheme = "BearerToken";
})
    .AddBearerToken(options =>
    {
        // ReSharper disable once RedundantDelegateCreation
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
            }

            string? accessToken = authHeader.ToString().Split("Bearer ").LastOrDefault();
            if (string.IsNullOrEmpty(accessToken))
            {
                message.Fail("No token provided");
                await Task.CompletedTask;
            }

            RestClient client = new($"{Globals.TwitchAuthUrl}/validate");
            RestRequest request = new();
            request.AddHeader("Authorization", $"OAuth {accessToken}");

            RestResponse response = await client.ExecuteAsync(request);
            if (!response.IsSuccessful)
            {
                message.Fail("Failed to validate token");
                await Task.CompletedTask;
            }

            ValidatedTokenResponse? user = response.Content?.FromJson<ValidatedTokenResponse>();
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
    })
    .AddTwitch(options =>
    {
        options.ClientId = Globals.ClientId;
        options.ClientSecret = Globals.ClientSecret;
        options.UsePkce = true;
        options.SaveTokens = true;
        options.Scope.Add("user:read:email");
    });

// Add Other Services
builder.Services.AddCors();
builder.Services.AddDirectoryBrowser();
// builder.Services.AddResponseCaching();
builder.Services.AddMvc(option => option.EnableEndpointRouting = false);
builder.Services.AddEndpointsApiExplorer();

// Add API versioning
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

// Add Swagger
builder.Services.AddSwaggerGen(options => options.OperationFilter<SwaggerDefaultValues>());
builder.Services.AddSwaggerGenNewtonsoftSupport();
builder.Services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();

builder.Services.AddHttpContextAccessor();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowOrigins", configurePolicy =>
    {
        configurePolicy
            .WithOrigins("https://nomercy.tv")
            .WithOrigins("https://*.nomercy.tv")
            .WithOrigins("http://localhost:5001")
            .WithOrigins("https://localhost")
            .AllowAnyMethod()
            .AllowCredentials()
            .SetIsOriginAllowedToAllowWildcardSubdomains()
            .WithHeaders("Access-Control-Allow-Private-Network", "true")
            .WithHeaders("Access-Control-Allow-Headers", "*")
            .AllowAnyHeader();
    });
});

builder.Services.AddResponseCompression(options => { options.EnableForHttps = true; });

builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Debug);
});

WebApplication app = builder.Build();

string[] supportedCultures = ["en-US", "nl-NL"]; // Add other supported locales
RequestLocalizationOptions localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture(supportedCultures[0])
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);

localizationOptions.FallBackToParentCultures = true;
localizationOptions.FallBackToParentUICultures = true;

app.UseRequestLocalization(localizationOptions);

app.UseRouting();
app.UseCors("AllowOrigins");

app.UseHsts();
app.UseHttpsRedirection();

app.UseResponseCompression();
app.UseRequestLocalization();

app.UseAuthentication();
app.UseAuthorization();
        
app.UseForwardedHeaders(new()
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseDeveloperExceptionPage();
app.UseSwagger();
app.UseSwaggerUI(ModernStyle.Dark, options =>
{
    options.DocumentTitle = "Twitch Shoutout Bot API";
    options.OAuthClientId(Globals.ClientId);
    options.OAuthClientSecret(Globals.ClientSecret);
    options.OAuthScopes("user:read:email");
    options.EnablePersistAuthorization();
    options.EnableTryItOutByDefault();
    
    IApiVersionDescriptionProvider provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
    IReadOnlyList<ApiVersionDescription> descriptions = provider.ApiVersionDescriptions;
    
    foreach (ApiVersionDescription description in descriptions)
    {
        string url = $"/swagger/{description.GroupName}/swagger.json";
        string name = description.GroupName.ToUpperInvariant();
        options.SwaggerEndpoint(url, name);
    }
});

app.UseMvcWithDefaultRoute();

using BotDbContext dbContext = new();
dbContext.Database.EnsureCreated();

app.Run();
