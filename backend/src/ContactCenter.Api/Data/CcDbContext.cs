using ContactCenter.Api.Auth;
using Microsoft.EntityFrameworkCore;

namespace ContactCenter.Api.Data;

/// <summary>
/// EF-context met tenant-isolatie. Tenant-eigen entiteiten dragen een <c>TenantId</c> en worden
/// via een global query filter automatisch beperkt tot de tenant van de huidige stroom
/// (<see cref="ITenantAccessor"/>). Buiten een tenant-context (achtergrondprocessen) is de
/// accessor <c>null</c> en levert elke gefilterde query 0 rijen — gebruik daar bewust
/// <c>IgnoreQueryFilters()</c>. De <see cref="Tenants"/>-tabel zelf is niet gefilterd.
/// </summary>
public sealed class CcDbContext(DbContextOptions<CcDbContext> options, ITenantAccessor tenant)
    : DbContext(options)
{
    /// <summary>De tenant van de huidige stroom; gooit als er geen tenant-context is (bv. een
    /// create-pad dat buiten een request draait). Reads worden al via de query-filter gescoped.</summary>
    public int CurrentTenantId =>
        tenant.TenantId ?? throw new InvalidOperationException("Geen tenant-context voor deze bewerking.");

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<QueueConfig> Queues => Set<QueueConfig>();
    public DbSet<InboundNumber> InboundNumbers => Set<InboundNumber>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<GlobalSettings> Settings => Set<GlobalSettings>();
    public DbSet<Contact> Contacts => Set<Contact>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(e =>
        {
            e.Property(t => t.Slug).HasMaxLength(40);
            e.HasIndex(t => t.Slug).IsUnique();
            e.Property(t => t.DisplayName).HasMaxLength(100);
            e.Property(t => t.Realm).HasMaxLength(80);
            e.HasIndex(t => t.Realm).IsUnique();
        });

        modelBuilder.Entity<QueueConfig>(e =>
        {
            e.Property(q => q.Name).HasMaxLength(40);
            e.HasIndex(q => new { q.TenantId, q.Name }).IsUnique();
            e.HasQueryFilter(q => q.TenantId == tenant.TenantId);
            e.Property(q => q.DisplayName).HasMaxLength(100);
            e.Property(q => q.WelcomePrompt).HasMaxLength(200);
            e.Property(q => q.ClosedPrompt).HasMaxLength(200);
            e.Property(q => q.AdHocForwardNumber).HasMaxLength(20);
            e.Property(q => q.TimeZone).HasMaxLength(60);
            e.Property(q => q.MusicOnHoldClass).HasMaxLength(60).HasDefaultValue("default");
            e.Property(q => q.OfferMode).HasConversion<string>().HasMaxLength(20)
                .HasDefaultValue(QueueOfferMode.AutoDispatch);
            e.Property(q => q.RoutingStrategy).HasConversion<string>().HasMaxLength(20)
                .HasDefaultValue(QueueRoutingStrategy.LongestIdle);
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
            e.HasIndex(a => new { a.TenantId, a.Name }).IsUnique();
            e.HasQueryFilter(a => a.TenantId == tenant.TenantId);
            e.Property(a => a.DisplayName).HasMaxLength(100);
            e.Property(a => a.Endpoint).HasMaxLength(60);
            // Endpoint is Asterisk-breed (tenant-overstijgend) en moet daarom globaal uniek zijn.
            e.HasIndex(a => a.Endpoint).IsUnique();
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

        modelBuilder.Entity<GlobalSettings>(e =>
        {
            e.HasIndex(s => s.TenantId).IsUnique();
            e.HasQueryFilter(s => s.TenantId == tenant.TenantId);
        });

        modelBuilder.Entity<Contact>(e =>
        {
            e.HasQueryFilter(c => c.TenantId == tenant.TenantId);
            e.Property(c => c.Name).HasMaxLength(100);
            e.HasIndex(c => c.Name);
            e.Property(c => c.Number).HasMaxLength(20);
            e.Property(c => c.Department).HasMaxLength(100);
        });
    }
}
