using Microsoft.EntityFrameworkCore;
using MiniInsurance.Data;
using MiniInsurance.Models;

namespace MiniInsurance.Services;

public record InsDash(int Active, int ExpiringSoon, int OpenClaims, decimal PremiumMonth, int Policies,
    List<(InsuranceType Type, int Count)> ByType);

public interface IInsuranceService
{
    Task<List<Insurer>> InsurersAsync();
    Task<int> CreateInsurerAsync(Insurer i);
    Task<List<Policy>> PoliciesAsync(PolicyStatus? status, string? q);
    Task<Policy?> GetPolicyAsync(int id);
    Task<Policy?> GetByPlateAsync(string plate);
    Task<int> CreatePolicyAsync(Policy p);
    Task<(bool ok, string msg)> AddReceiptAsync(int policyId, decimal amount, string method);
    Task CancelPolicyAsync(int id);
    Task<int> FileClaimAsync(int policyId, DateTime incidentDate, string desc, decimal amount);
    Task<(bool ok, string msg)> SetClaimStatusAsync(int claimId, ClaimStatus st);
    Task<InsDash> DashboardAsync();
}

public class InsuranceService(AppDbContext db) : IInsuranceService
{
    public Task<List<Insurer>> InsurersAsync() => db.Insurers.OrderBy(i => i.Name).ToListAsync();
    public async Task<int> CreateInsurerAsync(Insurer i)
    {
        if (string.IsNullOrWhiteSpace(i.Code)) i.Code = $"BH{await db.Insurers.CountAsync() + 1:D2}";
        db.Insurers.Add(i); await db.SaveChangesAsync(); return i.Id;
    }

    private async Task ExpireSweepAsync()
    {
        var stale = await db.Policies.Where(p => p.Status == PolicyStatus.Active && p.EndDate < DateTime.Today).ToListAsync();
        if (stale.Count > 0) { foreach (var p in stale) p.Status = PolicyStatus.Expired; await db.SaveChangesAsync(); }
    }

    public async Task<List<Policy>> PoliciesAsync(PolicyStatus? status, string? q)
    {
        await ExpireSweepAsync();
        var query = db.Policies.Include(p => p.Insurer).Include(p => p.Receipts).AsQueryable();
        if (status.HasValue) query = query.Where(p => p.Status == status.Value);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(p => p.Code.Contains(q) || p.VehiclePlate.Contains(q) || p.CustomerName.Contains(q));
        var list = await query.ToListAsync();
        return list.OrderByDescending(p => p.CreatedAt).ToList();
    }

    public Task<Policy?> GetPolicyAsync(int id) =>
        db.Policies.Include(p => p.Insurer).Include(p => p.Receipts).Include(p => p.Claims).FirstOrDefaultAsync(p => p.Id == id);

    public Task<Policy?> GetByPlateAsync(string plate) =>
        db.Policies.Include(p => p.Insurer).Where(p => p.VehiclePlate == plate.Trim())
          .OrderByDescending(p => p.EndDate).FirstOrDefaultAsync();

    public async Task<int> CreatePolicyAsync(Policy p)
    {
        p.Code = $"HDBH{DateTime.Now:yyMM}-{await db.Policies.CountAsync() + 1:D4}";
        p.Status = PolicyStatus.Quoted;
        db.Policies.Add(p); await db.SaveChangesAsync(); return p.Id;
    }

    public async Task<(bool ok, string msg)> AddReceiptAsync(int policyId, decimal amount, string method)
    {
        var p = await db.Policies.Include(x => x.Receipts).FirstOrDefaultAsync(x => x.Id == policyId);
        if (p == null) return (false, "Không tìm thấy hợp đồng.");
        if (p.Status == PolicyStatus.Cancelled) return (false, "Hợp đồng đã hủy.");
        db.Receipts.Add(new Receipt { PolicyId = policyId, Amount = amount, Method = method });
        // Đủ phí → kích hoạt hợp đồng.
        if (p.Paid + amount >= p.Premium && p.Status == PolicyStatus.Quoted) p.Status = PolicyStatus.Active;
        await db.SaveChangesAsync();
        return (true, "Đã ghi biên nhận thu phí.");
    }

    public async Task CancelPolicyAsync(int id)
    {
        var p = await db.Policies.FirstOrDefaultAsync(x => x.Id == id) ?? throw new KeyNotFoundException();
        p.Status = PolicyStatus.Cancelled; await db.SaveChangesAsync();
    }

    public async Task<int> FileClaimAsync(int policyId, DateTime incidentDate, string desc, decimal amount)
    {
        var c = new Claim { PolicyId = policyId, IncidentDate = incidentDate, Description = desc, ClaimAmount = amount,
            Code = $"BT{DateTime.Now:yyMM}-{await db.Claims.CountAsync() + 1:D3}" };
        db.Claims.Add(c); await db.SaveChangesAsync(); return c.Id;
    }

    public async Task<(bool ok, string msg)> SetClaimStatusAsync(int claimId, ClaimStatus st)
    {
        var c = await db.Claims.FirstOrDefaultAsync(x => x.Id == claimId);
        if (c == null) return (false, "Không tìm thấy yêu cầu bồi thường.");
        // Filed → Approved/Rejected; Approved → Paid.
        bool ok = st switch
        {
            ClaimStatus.Approved or ClaimStatus.Rejected => c.Status == ClaimStatus.Filed,
            ClaimStatus.Paid => c.Status == ClaimStatus.Approved,
            _ => false
        };
        if (!ok) return (false, "Chuyển trạng thái bồi thường không hợp lệ.");
        c.Status = st; await db.SaveChangesAsync();
        return (true, st switch { ClaimStatus.Approved => "Đã duyệt bồi thường.", ClaimStatus.Rejected => "Đã từ chối.", _ => "Đã chi trả bồi thường." });
    }

    public async Task<InsDash> DashboardAsync()
    {
        await ExpireSweepAsync();
        var policies = await db.Policies.ToListAsync();
        var claims = await db.Claims.ToListAsync();
        var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var premium = await db.Receipts.Where(r => r.PaidAt >= monthStart).SumAsync(r => (decimal?)r.Amount) ?? 0;
        var byType = policies.GroupBy(p => p.Type).Select(g => (g.Key, g.Count())).ToList();
        return new InsDash(
            policies.Count(p => p.Status == PolicyStatus.Active),
            policies.Count(p => p.Status == PolicyStatus.Active && p.EndDate <= DateTime.Today.AddDays(30) && p.EndDate >= DateTime.Today),
            claims.Count(c => c.Status is ClaimStatus.Filed or ClaimStatus.Approved),
            premium, policies.Count, byType);
    }
}
