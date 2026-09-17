namespace WebDatLichKhamBenh.Application.DTOs.Accounts;

// Dùng nội bộ giữa Application và API để tạo/kiểm tra cookie, không trả SessionId cho client.
public record AuthSessionDto(Guid SessionId, DateTime ExpiresAt, AccountDto Account);
