using Microsoft.EntityFrameworkCore;
using MiniInsurance.Data;
using MiniInsurance.Models;
using MiniInsurance.Services;
using Serilog;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
FleetObs.ConfigureLogger("miniinsurance");

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();
builder.WebHost.UseUrls($"http://0.0.0.0:{Environment.GetEnvironmentVariable("PORT") ?? "8080"}");

var conn = Environment.GetEnvironmentVariable("CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=miniinsurance.db";
builder.Services.AddDbContext<AppDbContext>(o =>
{
    if (DbUtil.IsPostgres(conn)) o.UseNpgsql(DbUtil.ToNpgsql(conn));
    else o.UseSqlite(conn);
});
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<IInsuranceService, InsuranceService>();
builder.Services.AddFleetObs();
builder.Services.AddControllersWithViews();

var app = builder.Build();
using (var scope = app.Services.CreateScope())
    await Seeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());

app.UseFleetObs();

app.Use(async (ctx, next) =>
{
    var key = ctx.Request.Headers["X-Api-Key"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(key)) ctx.Request.Cookies.TryGetValue(TenantContext.CookieName, out key);
    if (!string.IsNullOrWhiteSpace(key))
    {
        using var lookup = app.Services.CreateScope();
        var ldb = lookup.ServiceProvider.GetRequiredService<AppDbContext>();
        var org = await ldb.Orgs.FirstOrDefaultAsync(o => o.ApiKey == key);
        if (org != null) ctx.RequestServices.GetRequiredService<ITenantContext>().OrgId = org.Id;
    }
    await next();
});

app.UseStaticFiles();
app.MapGet("/healthz", () => "ok");

// API tra cứu bảo hiểm theo biển số (MiniShowroom/MiniService kiểm tra xe còn hạn BH)
app.MapGet("/api/policy", async (string plate, IInsuranceService svc) =>
{
    var p = await svc.GetByPlateAsync(plate);
    if (p == null) return Results.NotFound(new { plate, insured = false });
    return Results.Ok(new
    {
        plate = p.VehiclePlate, p.Code, insurer = p.Insurer.Name, type = Ui.Type(p.Type),
        status = Ui.Status(p.Status).text, sumInsured = p.SumInsured, endDate = p.EndDate.ToString("yyyy-MM-dd"),
        insured = p.Status == PolicyStatus.Active, daysToExpiry = p.DaysToExpiry
    });
});

// API tích hợp: MiniShowroom giao xe → tự lập bảo hiểm TNDS bắt buộc cho xe mới (chọn công ty BH đầu tiên).
app.MapPost("/api/ext/auto-policy", async (AutoPolicyDto dto, IInsuranceService svc) =>
{
    var insurers = await svc.InsurersAsync();
    if (insurers.Count == 0) return Results.BadRequest(new { error = "Chưa có công ty bảo hiểm." });
    if (string.IsNullOrWhiteSpace(dto.Plate)) return Results.BadRequest(new { error = "Cần biển số." });
    var sum = dto.SumInsured > 0 ? dto.SumInsured : 500_000_000;
    var id = await svc.CreatePolicyAsync(new Policy
    {
        CustomerName = dto.CustomerName ?? "Khách mua xe", CustomerPhone = dto.CustomerPhone,
        VehiclePlate = dto.Plate.Trim(), VehicleModel = dto.VehicleModel,
        InsurerId = insurers[0].Id, Type = InsuranceType.CompulsoryTPL,
        SumInsured = sum, Premium = Math.Round(sum * 0.0015m, 0),   // ~0.15% TNDS
        StartDate = DateTime.Today, EndDate = DateTime.Today.AddYears(1)
    });
    var p0 = await svc.GetPolicyAsync(id);
    // TNDS bắt buộc đóng ngay khi mua xe → ghi biên nhận đủ phí để kích hoạt hợp đồng.
    await svc.AddReceiptAsync(id, p0!.Premium, "Tiền mặt");
    var p = await svc.GetPolicyAsync(id);
    return Results.Ok(new { policyId = id, code = p!.Code, insurer = insurers[0].Name, premium = p.Premium, status = Ui.Status(p.Status).text });
});

// API tích hợp: MiniService lập yêu cầu bồi thường cho xe còn bảo hiểm (sửa sau tai nạn).
app.MapPost("/api/ext/claim", async (ExtClaimDto dto, IInsuranceService svc) =>
{
    if (string.IsNullOrWhiteSpace(dto.Plate)) return Results.BadRequest(new { error = "Cần biển số." });
    var p = await svc.GetByPlateAsync(dto.Plate);
    if (p == null) return Results.NotFound(new { error = "Xe chưa có hợp đồng bảo hiểm." });
    if (p.Status != PolicyStatus.Active) return Results.BadRequest(new { error = "Hợp đồng chưa/không còn hiệu lực.", status = Ui.Status(p.Status).text });
    if (dto.Amount <= 0) return Results.BadRequest(new { error = "Số tiền yêu cầu phải > 0." });
    var incident = DateTime.TryParse(dto.IncidentDate, out var d) ? d : DateTime.Today;
    var id = await svc.FileClaimAsync(p.Id, incident, dto.Description ?? "Sửa chữa sau va chạm", dto.Amount);
    var c = (await svc.GetPolicyAsync(p.Id))!.Claims.First(x => x.Id == id);
    return Results.Ok(new { claimId = id, code = c.Code, policyCode = p.Code, status = Ui.Claim(c.Status).text, amount = c.ClaimAmount });
});

// Import công ty bảo hiểm thật từ Mst_InsuranceCompany (SQL nguồn 2010.HTC). Dedupe theo Code.
app.MapPost("/api/import/insurers", async (List<ImportInsurerDto> rows, AppDbContext db) =>
{
    if (rows is null || rows.Count == 0) return Results.BadRequest(new { error = "Không có dữ liệu import." });
    int added = 0, skipped = 0;
    foreach (var r in rows)
    {
        if (string.IsNullOrWhiteSpace(r.InsCompanyCode) || string.IsNullOrWhiteSpace(r.InsCompanyName)) { skipped++; continue; }
        var code = r.InsCompanyCode.Trim();
        if (await db.Insurers.AnyAsync(i => i.Code == code)) { skipped++; continue; }
        db.Insurers.Add(new Insurer { Code = code, Name = r.InsCompanyName.Trim() });
        added++;
    }
    await db.SaveChangesAsync();
    return Results.Ok(new { added, skipped, total = rows.Count });
});

app.MapPost("/api/orgs/register", async (RegisterOrgDto dto, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(dto.Name)) return Results.BadRequest(new { error = "Cần Name." });
    var org = new Org { Name = dto.Name.Trim(), ApiKey = "ins_" + Guid.NewGuid().ToString("N") };
    db.Orgs.Add(org); await db.SaveChangesAsync();
    return Results.Ok(new { orgId = org.Id, apiKey = org.ApiKey });
});

app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
app.Run();

record RegisterOrgDto(string Name);
record ImportInsurerDto(string? InsCompanyCode, string? InsCompanyName);
record AutoPolicyDto(string Plate, string? VehicleModel, string? CustomerName, string? CustomerPhone, decimal SumInsured);
record ExtClaimDto(string Plate, decimal Amount, string? Description, string? IncidentDate);
