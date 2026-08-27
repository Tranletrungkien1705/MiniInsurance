using Microsoft.EntityFrameworkCore;
using MiniInsurance.Models;

namespace MiniInsurance.Data;

public static class Seeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        await db.Database.EnsureCreatedAsync();
        await MigratePostgresAsync(db);

        if (!await db.Orgs.AnyAsync(o => o.Id == TenantContext.DefaultOrgId))
        {
            db.Orgs.Add(new Org { Id = TenantContext.DefaultOrgId, Name = "Demo Insurance", ApiKey = TenantContext.DefaultApiKey });
            await db.SaveChangesAsync();
        }
        if (!await db.Insurers.AnyAsync())
        {
            db.Insurers.AddRange(
                new Insurer { Code = "PVI", Name = "Bảo hiểm PVI", Hotline = "1900545458" },
                new Insurer { Code = "BV", Name = "Bảo Việt", Hotline = "1900558899" },
                new Insurer { Code = "PTI", Name = "Bảo hiểm Bưu điện PTI", Hotline = "1900545475" });
            await db.SaveChangesAsync();
        }
        if (!await db.Policies.AnyAsync())
        {
            var ins = await db.Insurers.ToListAsync();
            int IId(string c) => ins.First(i => i.Code == c).Id;
            int n = 0;
            Policy P(string cust, string phone, string plate, string model, string insCode, InsuranceType type, decimal sum, decimal prem, PolicyStatus st, int endOffsetDays)
            {
                n++;
                return new Policy { Code = $"HDBH{DateTime.Now:yyMM}-{n:D4}", CustomerName = cust, CustomerPhone = phone, VehiclePlate = plate,
                    VehicleModel = model, InsurerId = IId(insCode), Type = type, SumInsured = sum, Premium = prem, Status = st,
                    StartDate = DateTime.Today.AddDays(endOffsetDays - 365), EndDate = DateTime.Today.AddDays(endOffsetDays),
                    Receipts = st == PolicyStatus.Active ? new List<Receipt> { new() { Amount = prem, Method = "Chuyển khoản", PaidAt = DateTime.Now.AddDays(-30) } } : new() };
            }
            db.Policies.AddRange(
                P("Nguyễn Văn An", "0901111111", "30A-123.45", "Hyundai Accent", "PVI", InsuranceType.PhysicalDamage, 567_000_000, 5_670_000, PolicyStatus.Active, 300),
                P("Trần Thị Bình", "0902222222", "51G-678.90", "Hyundai Tucson", "BV", InsuranceType.CompulsoryTPL, 100_000_000, 480_700, PolicyStatus.Active, 20),   // sắp hết hạn
                P("Lê Hoàng Cường", "0903333333", "29H-111.22", "Hyundai Santa Fe", "PTI", InsuranceType.PhysicalDamage, 1_365_000_000, 13_650_000, PolicyStatus.Quoted, 365));
            await db.SaveChangesAsync();
        }
    }

    private static async Task MigratePostgresAsync(AppDbContext db)
    {
        if (!db.Database.IsNpgsql()) return;
        var def = TenantContext.DefaultOrgId;
        var tables = new[] { "Insurers", "Policies", "Receipts", "Claims" };
        var sql = new List<string>
        {
            "CREATE TABLE IF NOT EXISTS miniinsurance.\"Orgs\" (\"Id\" uuid PRIMARY KEY, \"Name\" text NOT NULL DEFAULT '', \"ApiKey\" text NOT NULL DEFAULT '', \"CreatedAt\" timestamp NOT NULL DEFAULT now())",
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Orgs_ApiKey\" ON miniinsurance.\"Orgs\" (\"ApiKey\")",
        };
        foreach (var t in tables) sql.Add($"ALTER TABLE miniinsurance.\"{t}\" ADD COLUMN IF NOT EXISTS \"OrgId\" uuid NOT NULL DEFAULT '{def}'");
        foreach (var s in sql) try { await db.Database.ExecuteSqlRawAsync(s); } catch { }
    }
}
