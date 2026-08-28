using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MiniInsurance.Data;
using MiniInsurance.Models;
using MiniInsurance.Services;
using Xunit;

namespace MiniInsurance.Tests;

/// <summary>Test bảo hiểm xe: đủ phí → Active, hủy, bồi thường Filed→Approved→Paid (guard), sắp hết hạn.</summary>
public class InsuranceServiceTests
{
    private static (AppDbContext db, IInsuranceService svc, SqliteConnection conn) NewSvc()
    {
        var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        var opt = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(opt, new TenantContext { OrgId = TenantContext.DefaultOrgId });
        db.Database.EnsureCreated();
        return (db, new InsuranceService(db), conn);
    }

    private static async Task<int> NewPolicy(IInsuranceService svc, decimal premium = 5_000_000, int days = 365)
    {
        var iid = await svc.CreateInsurerAsync(new Insurer { Code = "PVI", Name = "PVI" });
        return await svc.CreatePolicyAsync(new Policy
        {
            CustomerName = "KH A", VehiclePlate = "30A-12345", InsurerId = iid, Type = InsuranceType.CompulsoryTPL,
            SumInsured = 100_000_000, Premium = premium, StartDate = DateTime.Today, EndDate = DateTime.Today.AddDays(days)
        });
    }

    [Fact]
    public async Task Policy_StartsQuoted()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var id = await NewPolicy(svc);
            Assert.Equal(PolicyStatus.Quoted, (await svc.GetPolicyAsync(id))!.Status);
        }
    }

    [Fact]
    public async Task FullPremium_Activates()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var id = await NewPolicy(svc, 5_000_000);
            await svc.AddReceiptAsync(id, 5_000_000, "Tiền mặt");
            Assert.Equal(PolicyStatus.Active, (await svc.GetPolicyAsync(id))!.Status);
        }
    }

    [Fact]
    public async Task PartialPremium_StaysQuoted()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var id = await NewPolicy(svc, 5_000_000);
            await svc.AddReceiptAsync(id, 2_000_000, "Tiền mặt");
            Assert.Equal(PolicyStatus.Quoted, (await svc.GetPolicyAsync(id))!.Status);
        }
    }

    [Fact]
    public async Task Cancel_SetsCancelled_AndBlocksReceipt()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var id = await NewPolicy(svc);
            await svc.CancelPolicyAsync(id);
            Assert.Equal(PolicyStatus.Cancelled, (await svc.GetPolicyAsync(id))!.Status);
            var (ok, _) = await svc.AddReceiptAsync(id, 1_000_000, "Tiền mặt");
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task Claim_Flow_FiledApprovedPaid()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var id = await NewPolicy(svc);
            var cid = await svc.FileClaimAsync(id, DateTime.Today, "Va chạm", 10_000_000);
            var (a, _) = await svc.SetClaimStatusAsync(cid, ClaimStatus.Approved);
            Assert.True(a);
            var (p, _) = await svc.SetClaimStatusAsync(cid, ClaimStatus.Paid);
            Assert.True(p);
            var claim = await db.Claims.FirstAsync(x => x.Id == cid);
            Assert.Equal(ClaimStatus.Paid, claim.Status);
        }
    }

    [Fact]
    public async Task Claim_CannotPay_WithoutApprove()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var id = await NewPolicy(svc);
            var cid = await svc.FileClaimAsync(id, DateTime.Today, "X", 1_000_000);
            var (ok, _) = await svc.SetClaimStatusAsync(cid, ClaimStatus.Paid);  // bỏ qua duyệt
            Assert.False(ok);
        }
    }

    [Fact]
    public async Task ExpiringSoon_FlaggedWithin30Days()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var id = await NewPolicy(svc, 1_000_000, days: 15);
            await svc.AddReceiptAsync(id, 1_000_000, "Tiền mặt");  // Active
            var p = await svc.GetPolicyAsync(id);
            Assert.True(p!.IsExpiringSoon);
        }
    }

    [Fact]
    public async Task GetByPlate_ReturnsActivePolicy()
    {
        var (db, svc, conn) = NewSvc(); using (conn)
        {
            var id = await NewPolicy(svc, 1_000_000);
            await svc.AddReceiptAsync(id, 1_000_000, "Tiền mặt");
            var p = await svc.GetByPlateAsync("30A-12345");
            Assert.NotNull(p);
        }
    }
}
