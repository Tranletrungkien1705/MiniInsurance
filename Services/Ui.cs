using MiniInsurance.Models;

namespace MiniInsurance.Services;

public static class Ui
{
    public static string Type(InsuranceType t) => t switch
    {
        InsuranceType.CompulsoryTPL => "TNDS bắt buộc",
        InsuranceType.PhysicalDamage => "Vật chất thân vỏ",
        InsuranceType.PassengerAccident => "Tai nạn người ngồi",
        _ => t.ToString()
    };
    public static (string text, string css) Status(PolicyStatus s) => s switch
    {
        PolicyStatus.Quoted => ("Chờ đóng phí", "secondary"),
        PolicyStatus.Active => ("Hiệu lực", "success"),
        PolicyStatus.Expired => ("Hết hạn", "warning"),
        PolicyStatus.Cancelled => ("Đã hủy", "dark"),
        _ => (s.ToString(), "secondary")
    };
    public static (string text, string css) Claim(ClaimStatus s) => s switch
    {
        ClaimStatus.Filed => ("Đã khai báo", "info"),
        ClaimStatus.Approved => ("Đã duyệt", "primary"),
        ClaimStatus.Rejected => ("Từ chối", "danger"),
        ClaimStatus.Paid => ("Đã chi trả", "success"),
        _ => (s.ToString(), "secondary")
    };
}
