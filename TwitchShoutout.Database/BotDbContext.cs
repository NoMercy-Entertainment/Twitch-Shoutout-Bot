using Microsoft.EntityFrameworkCore;
using TwitchShoutout.Database.Models;

namespace TwitchShoutout.Database;

public class BotDbContext : DbContext
{
    private static readonly string AppDataPath =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static readonly string DatabasePath = Path.Combine(AppDataPath, "TwitchShoutoutBot", "twitchbot.db");
    
    public BotDbContext()
    {
        string dbFolder = Path.Combine(AppDataPath, "TwitchShoutoutBot");
        if (!Directory.Exists(dbFolder)) Directory.CreateDirectory(dbFolder);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (optionsBuilder.IsConfigured) return;

        optionsBuilder.UseSqlite($"Data Source={DatabasePath}; Pooling=True",
            o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
    }
    
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        configurationBuilder.Properties<string>()
            .HaveMaxLength(256);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.Model.GetEntityTypes()
            .SelectMany(t => t.GetProperties())
            .Where(p => p.Name is "CreatedAt" or "UpdatedAt")
            .ToList()
            .ForEach(p => p.SetDefaultValueSql("CURRENT_TIMESTAMP"));

        modelBuilder.Model.GetEntityTypes()
            .SelectMany(t => t.GetProperties())
            .Where(p => p.ClrType == typeof(DateTime) && p.IsNullable)
            .ToList()
            .ForEach(p => p.SetDefaultValue(null));

        modelBuilder.Model.GetEntityTypes()
            .SelectMany(t => t.GetForeignKeys())
            .ToList()
            .ForEach(p => p.DeleteBehavior = DeleteBehavior.Cascade);
        
        // Make sure to encrypt and decrypt the access and refresh tokens
        modelBuilder.Entity<TwitchUser>()
            .Property(e => e.AccessToken)
            .HasConversion(
                v => TokenStore.EncryptToken(v),
                v =>  TokenStore.DecryptToken(v));
        
        modelBuilder.Entity<TwitchUser>()
            .Property(e => e.RefreshToken)
            .HasConversion(
                v => TokenStore.EncryptToken(v),
                v =>  TokenStore.DecryptToken(v));
        
        modelBuilder.Entity<ChannelInfo>()
            .HasOne(ci => ci.Channel)
            .WithOne(c => c.Info)
            .HasForeignKey<ChannelInfo>(ci => ci.Id);
    }
    
    public DbSet<TwitchUser> TwitchUsers { get; set; } = null!;
    public DbSet<Channel> Channels { get; set; }
    // public DbSet<Command> Commands { get; set; }
    public DbSet<Shoutout> Shoutouts { get; set; }
    public DbSet<ChannelModerator> ChannelModerators { get; set; }
    public DbSet<Pronoun> Pronouns { get; set; }
    public DbSet<ChannelInfo> ChannelInfos { get; set; } = null!;

}