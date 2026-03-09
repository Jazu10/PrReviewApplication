using Microsoft.EntityFrameworkCore;
using AiPrReview.Core.Entities;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace AiPrReview.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<RepositoryConfig> Repositories { get; set; }
    public DbSet<LlmProvider> LlmProviders { get; set; }
    public DbSet<LlmProviderSetting> LlmProviderSettings { get; set; }
    public DbSet<ReviewConfig> ReviewConfigurations { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RepositoryConfig>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Provider).HasConversion<string>();
        });

        modelBuilder.Entity<LlmProvider>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasMany(e => e.Settings)
                  .WithOne()
                  .HasForeignKey(s => s.ProviderId);
        });

        modelBuilder.Entity<LlmProviderSetting>(entity =>
        {
            entity.HasKey(e => new { e.ProviderId, e.Key });
        });

        modelBuilder.Entity<ReviewConfig>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.RepositoryId).IsUnique();
            entity.Property(e => e.ReviewStrategy).HasConversion<string>();
        });
    }
}