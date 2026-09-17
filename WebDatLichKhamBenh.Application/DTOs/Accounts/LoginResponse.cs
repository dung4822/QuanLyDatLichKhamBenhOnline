namespace WebDatLichKhamBenh.Application.DTOs.Accounts;

public record LoginResponse(DateTime ExpiresAt, AccountDto Account);
