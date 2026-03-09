using Microsoft.EntityFrameworkCore;
using AiPrReview.Core.Entities;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace AiPrReview.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<ProviderConfig> ProviderConfigs { get; set; }
    public DbSet<LlmProvider> LlmProviders { get; set; }
    public DbSet<LlmProviderSetting> LlmProviderSettings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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

        modelBuilder.Entity<ProviderConfig>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Provider).HasConversion<string>();
            entity.Property(e => e.ReviewStrategy).HasConversion<string>();
            entity.Property(e => e.PublishMode).HasConversion<string>();
        });
    }
}