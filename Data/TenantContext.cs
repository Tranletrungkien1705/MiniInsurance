namespace MiniInsurance.Data;

public interface ITenantContext { Guid OrgId { get; set; } }

public sealed class TenantContext : ITenantContext
{
    public static readonly Guid DefaultOrgId = new("cccccccc-cccc-cccc-cccc-cccccccccccc");
    public const string DefaultApiKey = "demo-insurance";
    public const string CookieName = "org_key";
    public Guid OrgId { get; set; } = DefaultOrgId;
}
