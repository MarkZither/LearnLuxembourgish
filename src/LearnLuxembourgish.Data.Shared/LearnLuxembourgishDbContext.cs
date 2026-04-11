using LearnLuxembourgish.Data.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace LearnLuxembourgish.Data.Shared;

public class LearnLuxembourgishDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Translation> Translations => Set<Translation>();
    public DbSet<FlashCard> FlashCards => Set<FlashCard>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<TranslationAudio> TranslationAudios => Set<TranslationAudio>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Translation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SourceText).IsRequired().HasMaxLength(5000);
            entity.Property(e => e.SourceLanguage).IsRequired().HasMaxLength(10);
            entity.Property(e => e.TranslatedText).IsRequired().HasMaxLength(5000);
            entity.Property(e => e.GrammarExplanation).HasMaxLength(10000);
            entity.Property(e => e.TranslationProvider).HasMaxLength(50);
            entity.Property(e => e.AudioUrl).HasMaxLength(2048);
            entity.Property(e => e.UserId).HasMaxLength(256);
        });

        modelBuilder.Entity<FlashCard>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DeckName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.UserId).IsRequired().HasMaxLength(256);
            entity.HasOne(e => e.Translation)
                .WithMany(t => t.FlashCards)
                .HasForeignKey(e => e.TranslationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.PublicId).IsUnique();
            entity.HasIndex(e => e.Email);
            entity.HasIndex(e => new { e.AuthProvider, e.ExternalUserId });
            entity.Property(e => e.Email).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.AuthProvider).IsRequired().HasMaxLength(50);
            entity.Property(e => e.ExternalUserId).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Role).HasMaxLength(50);
            entity.Property(e => e.TimeZone).HasMaxLength(50);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.Property(e => e.TokenHash).IsRequired().HasMaxLength(256);
            entity.Property(e => e.DeviceId).HasMaxLength(256);
            entity.Property(e => e.RevocationReason).HasMaxLength(500);
            entity.HasOne(e => e.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TranslationAudio>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TextHash).IsUnique();
            entity.Property(e => e.LuxembourgishText).IsRequired().HasMaxLength(5000);
            entity.Property(e => e.TextHash).IsRequired().HasMaxLength(64);
            entity.Property(e => e.AudioData).IsRequired();
            entity.Property(e => e.ContentType).IsRequired().HasMaxLength(50);
        });
    }
}
