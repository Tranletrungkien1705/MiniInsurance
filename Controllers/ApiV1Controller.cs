using Microsoft.AspNetCore.Mvc;
using MiniInsurance.Data;
using MiniInsurance.Models;
using MiniInsurance.Services;

namespace MiniInsurance.Controllers;

/// <summary>
/// API JSON cho SPA React. DTO phẳng. Dashboard cache Redis 30s theo tenant (X-Cache).
/// HĐBH: Quoted → (đủ phí) Active → Expired/Cancelled. Bồi thường: Filed → Approved/Rejected → Paid.
/// </summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public class ApiV1Controller(IInsuranceService svc, ICache cache, ITenantContext tenant) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var key = $"ins:dash:{tenant.OrgId}";
        var hit = await cache.GetAsync<DashDto>(key);
        if (hit != null) { Response.Headers["X-Cache"] = "HIT"; return Ok(hit); }
        var d = await svc.DashboardAsync();
        var dto = new DashDto(d.Active, d.ExpiringSoon, d.OpenClaims, d.PremiumMonth, d.Policies,
            d.ByType.Select(x => new ByTypeDto(Ui.Type(x.Item1), x.Item2)).ToList());
        await cache.SetAsync(key, dto, TimeSpan.FromSeconds(30));
        Response.Headers["X-Cache"] = "MISS";
        return Ok(dto);
    }

    [HttpGet("insurers")]
    public async Task<IActionResult> Insurers()
        => Ok((await svc.InsurersAsync()).Select(i => new { i.Id, i.Code, i.Name, i.Hotline }));

    [HttpPost("insurers")]
    public async Task<IActionResult> CreateInsurer([FromBody] InsurerReq r)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return BadRequest(new { error = "Cần tên công ty BH." });
        var id = await svc.CreateInsurerAsync(new Insurer { Name = r.Name.Trim(), Code = r.Code ?? "", Hotline = r.Hotline });
        return Ok(new { id });
    }

    [HttpGet("policies")]
    public async Task<IActionResult> Policies([FromQuery] PolicyStatus? status, [FromQuery] string? q)
        => Ok((await svc.PoliciesAsync(status, q)).Select(ToListDto));

    [HttpGet("policies/{id:int}")]
    public async Task<IActionResult> Policy(int id)
    {
        var p = await svc.GetPolicyAsync(id);
        return p == null ? NotFound(new { error = "Không tìm thấy hợp đồng." }) : Ok(ToDetailDto(p));
    }

    [HttpPost("policies")]
    public async Task<IActionResult> CreatePolicy([FromBody] PolicyReq r)
    {
        if (string.IsNullOrWhiteSpace(r.CustomerName) || string.IsNullOrWhiteSpace(r.VehiclePlate))
            return BadRequest(new { error = "Cần tên khách và biển số xe." });
        if (r.InsurerId <= 0) return BadRequest(new { error = "Cần chọn công ty BH." });
        var id = await svc.CreatePolicyAsync(new Policy
        {
            CustomerName = r.CustomerName.Trim(), CustomerPhone = r.CustomerPhone, VehiclePlate = r.VehiclePlate.Trim(), VehicleModel = r.VehicleModel,
            InsurerId = r.InsurerId, Type = (InsuranceType)r.Type, SumInsured = r.SumInsured, Premium = r.Premium,
            StartDate = r.StartDate == default ? DateTime.Today : r.StartDate,
            EndDate = r.EndDate == default ? DateTime.Today.AddYears(1) : r.EndDate, Note = r.Note
        });
        return Ok(new { id });
    }

    [HttpPost("policies/{id:int}/receipt")]
    public async Task<IActionResult> Receipt(int id, [FromBody] ReceiptReq r)
    {
        var (ok, msg) = await svc.AddReceiptAsync(id, r.Amount, string.IsNullOrWhiteSpace(r.Method) ? "Tiền mặt" : r.Method!);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    [HttpPost("policies/{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id)
    {
        await svc.CancelPolicyAsync(id);
        return Ok(new { ok = true });
    }

    [HttpPost("policies/{id:int}/claims")]
    public async Task<IActionResult> FileClaim(int id, [FromBody] ClaimReq r)
    {
        var cid = await svc.FileClaimAsync(id, r.IncidentDate == default ? DateTime.Today : r.IncidentDate, r.Description ?? "", r.ClaimAmount);
        return Ok(new { id = cid });
    }

    [HttpPost("claims/{id:int}/status")]
    public async Task<IActionResult> ClaimStatus(int id, [FromBody] ClaimStatusReq r)
    {
        var (ok, msg) = await svc.SetClaimStatusAsync(id, (ClaimStatus)r.Status);
        return ok ? Ok(new { ok, msg }) : BadRequest(new { ok, error = msg });
    }

    private static object ToListDto(Policy p) => new
    {
        p.Id, p.Code, p.CustomerName, p.VehiclePlate, p.VehicleModel, insurer = p.Insurer?.Name,
        type = Ui.Type(p.Type), p.SumInsured, p.Premium, paid = p.Paid, p.StartDate, p.EndDate,
        status = (int)p.Status, statusText = Ui.Status(p.Status).text, statusCss = Ui.Status(p.Status).css,
        expiringSoon = p.IsExpiringSoon, daysToExpiry = p.DaysToExpiry
    };
    private static object ToDetailDto(Policy p) => new
    {
        p.Id, p.Code, p.CustomerName, p.CustomerPhone, p.VehiclePlate, p.VehicleModel, insurerId = p.InsurerId, insurer = p.Insurer?.Name,
        type = (int)p.Type, typeText = Ui.Type(p.Type), p.SumInsured, p.Premium, paid = p.Paid,
        p.StartDate, p.EndDate, status = (int)p.Status, statusText = Ui.Status(p.Status).text, statusCss = Ui.Status(p.Status).css, p.Note,
        receipts = p.Receipts.OrderByDescending(r => r.PaidAt).Select(r => new { r.Amount, r.Method, r.PaidAt }),
        claims = p.Claims.OrderByDescending(c => c.CreatedAt).Select(c => new { c.Id, c.Code, c.IncidentDate, c.Description, c.ClaimAmount, status = (int)c.Status, statusText = Ui.Claim(c.Status).text, statusCss = Ui.Claim(c.Status).css })
    };
}

public record DashDto(int Active, int ExpiringSoon, int OpenClaims, decimal PremiumMonth, int Policies, List<ByTypeDto> ByType);
public record ByTypeDto(string Type, int Count);

public class InsurerReq { public string Name { get; set; } = ""; public string? Code { get; set; } public string? Hotline { get; set; } }
public class PolicyReq
{
    public string CustomerName { get; set; } = ""; public string? CustomerPhone { get; set; }
    public string VehiclePlate { get; set; } = ""; public string? VehicleModel { get; set; }
    public int InsurerId { get; set; } public int Type { get; set; } public decimal SumInsured { get; set; } public decimal Premium { get; set; }
    public DateTime StartDate { get; set; } public DateTime EndDate { get; set; } public string? Note { get; set; }
}
public class ReceiptReq { public decimal Amount { get; set; } public string? Method { get; set; } }
public class ClaimReq { public DateTime IncidentDate { get; set; } public string? Description { get; set; } public decimal ClaimAmount { get; set; } }
public class ClaimStatusReq { public int Status { get; set; } }
