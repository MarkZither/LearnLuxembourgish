using LearnLuxembourgish.Data.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace LearnLuxembourgish.Data.Shared;

public class LearnLuxembourgishDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Translation> Translations => Set<Translation>();
    public DbSet<FlashCard> FlashCards => Set<FlashCard>();

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
    }
}
