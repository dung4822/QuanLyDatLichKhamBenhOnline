namespace WebDatLichKhamBenh.Application.Extensions;

public static class StringExtensions
{
    // "  abc " → "abc"; null, "" hoặc "   " → null. Dùng cho các field không bắt buộc.
    public static string? TrimToNull(this string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Email dùng để so sánh/tìm kiếm: bỏ khoảng trắng và không phân biệt hoa thường.
    public static string ToNormalizedEmail(this string email)
        => email.Trim().ToUpperInvariant();
}
