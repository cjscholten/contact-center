using Microsoft.EntityFrameworkCore;

namespace ContactCenter.Api.Data;

public sealed class CcDbContext(DbContextOptions<CcDbContext> options) : DbContext(options)
{
    public DbSet<QueueConfig> Queues => Set<QueueConfig>();
    public DbSet<InboundNumber> InboundNumbers => Set<InboundNumber>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<GlobalSettings> Settings => Set<GlobalSettings>();
    public DbSet<Contact> Contacts => Set<Contact>();

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
            e.Property(q => q.MusicOnHoldClass).HasMaxLength(60).HasDefaultValue("default");
            e.Property(q => q.WelcomeText).HasMaxLength(1000).HasDefaultValue("");
            e.Property(q => q.ClosedText).HasMaxLength(1000).HasDefaultValue("");
            e.Property(q => q.Voice).HasMaxLength(60).HasDefaultValue("nl_NL-pim-medium");
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

        modelBuilder.Entity<Agent>(e =>
        {
            e.Property(a => a.Name).HasMaxLength(40);
            e.HasIndex(a => a.Name).IsUnique();
            e.Property(a => a.DisplayName).HasMaxLength(100);
            e.Property(a => a.Endpoint).HasMaxLength(60);
            e.Property(a => a.SipPassword).HasMaxLength(100).HasDefaultValue("changeme-dev");
            e.HasMany(a => a.QueueAssignments).WithOne().HasForeignKey(qa => qa.AgentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AgentQueueAssignment>(e =>
        {
            e.HasKey(qa => new { qa.AgentId, qa.QueueConfigId });
            e.HasOne(qa => qa.Queue).WithMany().HasForeignKey(qa => qa.QueueConfigId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Contact>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(100);
            e.HasIndex(c => c.Name);
            e.Property(c => c.Number).HasMaxLength(20);
            e.Property(c => c.Department).HasMaxLength(100);
        });
    }
}
