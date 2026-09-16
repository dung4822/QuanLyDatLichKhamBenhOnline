using Microsoft.AspNetCore.Identity;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Infrastructure.Security;

public class PasswordService : IPasswordService
{
    private readonly PasswordHasher<UserAccount> _hasher = new();
    public string Hash(UserAccount account, string password) => _hasher.HashPassword(account, password);
    public bool Verify(UserAccount account, string password)
    {
        var result = _hasher.VerifyHashedPassword(account, account.PasswordHash, password);
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            account.PasswordHash = Hash(account, password);
        return result != PasswordVerificationResult.Failed;
    }
}
