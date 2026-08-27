using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniInsurance.Data;
using MiniInsurance.Models;
using MiniInsurance.Services;

namespace MiniInsurance.Controllers;

public class HomeController(IInsuranceService svc) : Controller
{
    public async Task<IActionResult> Index() { ViewBag.Dash = await svc.DashboardAsync(); return View(); }
}

public class InsurerController(IInsuranceService svc) : Controller
{
    public async Task<IActionResult> Index() => View(await svc.InsurersAsync());
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, string? code, string? hotline)
    {
        if (string.IsNullOrWhiteSpace(name)) { TempData["Error"] = "Cần tên công ty BH."; return RedirectToAction(nameof(Index)); }
        await svc.CreateInsurerAsync(new Insurer { Name = name.Trim(), Code = code ?? "", Hotline = hotline });
        TempData["Success"] = "Đã thêm công ty bảo hiểm.";
        return RedirectToAction(nameof(Index));
    }
}

public class PolicyController(IInsuranceService svc) : Controller
{
    public async Task<IActionResult> Index(PolicyStatus? status, string? q)
    {
        ViewBag.Status = status; ViewBag.Q = q;
        return View(await svc.PoliciesAsync(status, q));
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.Insurers = await svc.InsurersAsync();
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string customerName, string? customerPhone, string vehiclePlate, string? vehicleModel,
        int insurerId, InsuranceType type, decimal sumInsured, decimal premium, DateTime startDate, DateTime endDate, string? note)
    {
        if (string.IsNullOrWhiteSpace(customerName) || string.IsNullOrWhiteSpace(vehiclePlate) || insurerId <= 0)
        { TempData["Error"] = "Cần khách hàng, biển số, công ty BH."; ViewBag.Insurers = await svc.InsurersAsync(); return View(); }
        var id = await svc.CreatePolicyAsync(new Policy
        {
            CustomerName = customerName.Trim(), CustomerPhone = customerPhone, VehiclePlate = vehiclePlate.Trim(), VehicleModel = vehicleModel,
            InsurerId = insurerId, Type = type, SumInsured = sumInsured, Premium = premium,
            StartDate = startDate == default ? DateTime.Today : startDate,
            EndDate = endDate == default ? DateTime.Today.AddYears(1) : endDate, Note = note
        });
        TempData["Success"] = "Đã tạo hợp đồng (chờ đóng phí).";
        return RedirectToAction(nameof(Detail), new { id });
    }

    public async Task<IActionResult> Detail(int id)
    {
        var p = await svc.GetPolicyAsync(id);
        if (p == null) return NotFound();
        return View(p);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddReceipt(int id, decimal amount, string method)
    {
        var (ok, msg) = await svc.AddReceiptAsync(id, amount, string.IsNullOrWhiteSpace(method) ? "Tiền mặt" : method);
        TempData[ok ? "Success" : "Error"] = msg;
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        await svc.CancelPolicyAsync(id);
        TempData["Success"] = "Đã hủy hợp đồng.";
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> FileClaim(int id, DateTime incidentDate, string description, decimal claimAmount)
    {
        if (string.IsNullOrWhiteSpace(description)) { TempData["Error"] = "Cần mô tả sự cố."; return RedirectToAction(nameof(Detail), new { id }); }
        await svc.FileClaimAsync(id, incidentDate == default ? DateTime.Today : incidentDate, description, claimAmount);
        TempData["Success"] = "Đã khai báo yêu cầu bồi thường.";
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ClaimAction(int id, int claimId, ClaimStatus to)
    {
        var (ok, msg) = await svc.SetClaimStatusAsync(claimId, to);
        TempData[ok ? "Success" : "Error"] = msg;
        return RedirectToAction(nameof(Detail), new { id });
    }
}

public class OrgController(AppDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var orgs = await db.Orgs.IgnoreQueryFilters().OrderBy(o => o.CreatedAt).ToListAsync();
        Request.Cookies.TryGetValue(TenantContext.CookieName, out var curKey);
        ViewBag.CurrentKey = curKey ?? TenantContext.DefaultApiKey;
        return View(orgs);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) { TempData["Error"] = "Cần tên tổ chức."; return RedirectToAction(nameof(Index)); }
        var org = new Org { Name = name.Trim(), ApiKey = "ins_" + Guid.NewGuid().ToString("N") };
        db.Orgs.Add(org); await db.SaveChangesAsync();
        SetCookies(org.ApiKey, org.Name);
        TempData["Success"] = $"Đã tạo & chuyển sang \"{org.Name}\".";
        return RedirectToAction("Index", "Home");
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Switch(string apiKey)
    {
        var org = await db.Orgs.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.ApiKey == apiKey);
        if (org == null) { TempData["Error"] = "Không tìm thấy."; return RedirectToAction(nameof(Index)); }
        SetCookies(org.ApiKey, org.Name);
        return RedirectToAction("Index", "Home");
    }
    public IActionResult Reset()
    {
        Response.Cookies.Delete(TenantContext.CookieName); Response.Cookies.Delete("org_name");
        return RedirectToAction("Index", "Home");
    }
    private void SetCookies(string k, string n)
    {
        var o = new CookieOptions { IsEssential = true, Expires = DateTimeOffset.UtcNow.AddDays(30) };
        Response.Cookies.Append(TenantContext.CookieName, k, o); Response.Cookies.Append("org_name", n, o);
    }
}
