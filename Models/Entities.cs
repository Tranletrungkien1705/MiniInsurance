namespace MiniInsurance.Models;

public class Org
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
public interface IOrgOwned { Guid OrgId { get; set; } }

/// <summary>Loại bảo hiểm xe.</summary>
public enum InsuranceType { CompulsoryTPL = 0, PhysicalDamage = 1, PassengerAccident = 2 }
public enum PolicyStatus { Quoted = 0, Active = 1, Expired = 2, Cancelled = 3 }
public enum ClaimStatus { Filed = 0, Approved = 1, Rejected = 2, Paid = 3 }

/// <summary>Công ty bảo hiểm.</summary>
public class Insurer : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Hotline { get; set; }
}

/// <summary>Hợp đồng bảo hiểm xe.</summary>
public class Policy : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string Code { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string? CustomerPhone { get; set; }
    public string VehiclePlate { get; set; } = "";
    public string? VehicleModel { get; set; }
    public int InsurerId { get; set; }
    public InsuranceType Type { get; set; }
    public decimal SumInsured { get; set; }        // số tiền bảo hiểm
    public decimal Premium { get; set; }           // phí bảo hiểm
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime EndDate { get; set; } = DateTime.Today.AddYears(1);
    public PolicyStatus Status { get; set; } = PolicyStatus.Quoted;
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public Insurer Insurer { get; set; } = null!;
    public List<Receipt> Receipts { get; set; } = [];
    public List<Claim> Claims { get; set; } = [];

    public decimal Paid => Receipts.Sum(r => r.Amount);
    public bool IsExpiringSoon => Status == PolicyStatus.Active && EndDate <= DateTime.Today.AddDays(30) && EndDate >= DateTime.Today;
    public int DaysToExpiry => (EndDate.Date - DateTime.Today).Days;
}

/// <summary>Biên nhận thu phí bảo hiểm.</summary>
public class Receipt : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public int PolicyId { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; } = "Tiền mặt";
    public DateTime PaidAt { get; set; } = DateTime.Now;
    public Policy Policy { get; set; } = null!;
}

/// <summary>Yêu cầu bồi thường.</summary>
public class Claim : IOrgOwned
{
    public int Id { get; set; }
    public Guid OrgId { get; set; }
    public string Code { get; set; } = "";
    public int PolicyId { get; set; }
    public DateTime IncidentDate { get; set; } = DateTime.Today;
    public string Description { get; set; } = "";
    public decimal ClaimAmount { get; set; }
    public ClaimStatus Status { get; set; } = ClaimStatus.Filed;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public Policy Policy { get; set; } = null!;
}
