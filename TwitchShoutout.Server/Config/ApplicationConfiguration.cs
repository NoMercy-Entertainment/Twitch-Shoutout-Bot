using Asp.Versioning.ApiExplorer;
using AspNetCore.Swagger.Themes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using TwitchShoutout.Database;

namespace TwitchShoutout.Server.Config;

public static class ApplicationConfiguration
{
    public static void ConfigureApp(WebApplication app)
    {
        ConfigureLocalization(app);
        ConfigureMiddleware(app);
        ConfigureSwaggerUi(app);
        
        app.MapControllers();
        app.MapGet("/", () => Results.Redirect("/swagger"));
        
        EnsureDatabase(app);
    }

    private static void ConfigureLocalization(WebApplication app)
    {
        string[] supportedCultures = ["en-US", "nl-NL"];
        RequestLocalizationOptions options = new()
        {
            FallBackToParentCultures = true,
            FallBackToParentUICultures = true
        };
        
        options.SetDefaultCulture(supportedCultures[0])
            .AddSupportedCultures(supportedCultures)
            .AddSupportedUICultures(supportedCultures);

        app.UseRequestLocalization(options);
    }

    private static void ConfigureMiddleware(WebApplication app)
    {
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
    }

    private static void EnsureDatabase(WebApplication app)
    {
        using BotDbContext dbContext = new();
        dbContext.Database.EnsureCreated();
    }
    
    private static void ConfigureSwaggerUi(WebApplication app)
    {
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
    }
}