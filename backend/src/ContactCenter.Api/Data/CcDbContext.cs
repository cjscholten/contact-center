using Microsoft.EntityFrameworkCore;

namespace ContactCenter.Api.Data;

public sealed class CcDbContext(DbContextOptions<CcDbContext> options) : DbContext(options)
{
    public DbSet<QueueConfig> Queues => Set<QueueConfig>();
    public DbSet<InboundNumber> InboundNumbers => Set<InboundNumber>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<QueueConfig>(e =>
        {
            e.Property(q => q.Name).HasMaxLength(40);
            e.HasIndex(q => q.Name).IsUnique();
            e.Property(q => q.DisplayName).HasMaxLength(100);
            e.Property(q => q.WelcomePrompt).HasMaxLength(200);
            e.Property(q => q.ClosedPrompt).HasMaxLength(200);
            e.Property(q => q.AdHocForwardNumber).HasMaxLength(20);
            e.Property(q => q.TimeZone).HasMaxLength(60);
            e.HasMany(q => q.OpeningHours).WithOne().HasForeignKey(w => w.QueueConfigId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(q => q.Numbers).WithOne().HasForeignKey(n => n.QueueConfigId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InboundNumber>(e =>
        {
            e.Property(n => n.Number).HasMaxLength(20);
            e.HasIndex(n => n.Number).IsUnique();
        });
    }
}
