namespace WebDatLichKhamBenh.Domain.Enums;

// Trạng thái chuyên khoa đang lưu dạng chuỗi trong database, nên dùng hằng số thay vì enum
// để không phải đổi kiểu cột (migration). Viết hằng số một lần, tránh gõ sai "Actve" ở mỗi nơi.
public static class SpecialtyStatus
{
    public const string Active = "Active";
    public const string Inactive = "Inactive";

    public static bool IsActive(string? status)
        => string.Equals(status?.Trim(), Active, StringComparison.OrdinalIgnoreCase);
}
