using Microsoft.EntityFrameworkCore;
using MiniInsurance.Models;

namespace MiniInsurance.Data;

public class AppDbContext : DbContext
{
    private readonly Guid _orgId;
    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant) : base(options) => _orgId = tenant.OrgId;

    public DbSet<Org> Orgs => Set<Org>();
    public DbSet<Insurer> Insurers => Set<Insurer>();
    public DbSet<Policy> Policies => Set<Policy>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<Claim> Claims => Set<Claim>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        if (Database.IsNpgsql()) b.HasDefaultSchema("miniinsurance");
        b.Entity<Org>().HasIndex(x => x.ApiKey).IsUnique();
        b.Entity<Insurer>(e => { e.HasIndex(x => new { x.OrgId, x.Code }).IsUnique(); e.HasQueryFilter(x => x.OrgId == _orgId); });
        b.Entity<Policy>(e =>
        {
            e.HasIndex(x => new { x.OrgId, x.Code }).IsUnique();
            e.Property(x => x.SumInsured).HasPrecision(18, 2);
            e.Property(x => x.Premium).HasPrecision(18, 2);
            e.Ignore(x => x.Paid); e.Ignore(x => x.IsExpiringSoon); e.Ignore(x => x.DaysToExpiry);
            e.HasOne(x => x.Insurer).WithMany().HasForeignKey(x => x.InsurerId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<Receipt>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasOne(x => x.Policy).WithMany(x => x.Receipts).HasForeignKey(x => x.PolicyId);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
        b.Entity<Claim>(e =>
        {
            e.Property(x => x.ClaimAmount).HasPrecision(18, 2);
            e.HasOne(x => x.Policy).WithMany(x => x.Claims).HasForeignKey(x => x.PolicyId);
            e.HasQueryFilter(x => x.OrgId == _orgId);
        });
    }

    public override int SaveChanges() { StampOrg(); return base.SaveChanges(); }
    public override Task<int> SaveChangesAsync(CancellationToken ct = default) { StampOrg(); return base.SaveChangesAsync(ct); }
    private void StampOrg()
    {
        foreach (var e in ChangeTracker.Entries<IOrgOwned>())
            if (e.State == EntityState.Added && e.Entity.OrgId == Guid.Empty) e.Entity.OrgId = _orgId;
    }
}
