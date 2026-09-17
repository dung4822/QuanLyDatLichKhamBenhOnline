using WebDatLichKhamBenh.Application.DTOs.Accounts;
using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Application.Mappings;

public static class AccountMappings
{
    public static AccountDto ToDto(this UserAccount account)
    {
        return new AccountDto(account.UserAccountId, account.Email, account.DisplayName,
            account.Role, account.PatientId, account.DoctorId, account.IsActive);
    }
}
