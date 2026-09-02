using Microsoft.EntityFrameworkCore;
using StrategyOps.BuildingBlocks.Persistence;
using StrategyOps.Identity.Api.Domain;

namespace StrategyOps.Identity.Api.Infrastructure;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<PortfolioUser> Users => Set<PortfolioUser>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PortfolioUser>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(u => u.Id);
            entity.Property(u => u.UserName).HasMaxLength(80).IsRequired();
            entity.HasIndex(u => u.UserName).IsUnique();
            entity.Property(u => u.DisplayName).HasMaxLength(120).IsRequired();
            entity.Property(u => u.Role).HasMaxLength(40).IsRequired();
            entity.Property(u => u.PasswordHash).HasMaxLength(200).IsRequired();
            entity.Property(u => u.PasswordSalt).HasMaxLength(80).IsRequired();
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refresh_tokens");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.TokenHash).HasMaxLength(120).IsRequired();
            entity.HasIndex(t => t.TokenHash).IsUnique();
            entity.HasIndex(t => t.UserId);
        });

        modelBuilder.ApplyDateTimeOffsetConversions(Database.ProviderName);
    }
}
