using WebDatLichKhamBenh.Domain.Enums;

namespace WebDatLichKhamBenh.Application.DTOs.Accounts;

public record AccountDto(Guid UserAccountId, string Email, string DisplayName,
    AccountRole Role, int? PatientId, int? DoctorId, bool IsActive);
