using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Application.Interfaces.Repositories;

public interface IAccountRepository
{
    Task<UserAccount?> GetByIdAsync(Guid id);
    Task<UserAccount?> GetByEmailAsync(string normalizedEmail);
    Task<List<UserAccount>> GetPageAsync(int page, int pageSize);
    Task<bool> LinkExistsAsync(int? patientId, int? doctorId, Guid? excludeId = null);
    Task<int> CountActiveAdminsAsync();
    Task AddAsync(UserAccount account);
    Task AddSessionAsync(UserSession session);
    Task<UserSession?> FindSessionAsync(string tokenHash);
    Task DeleteSessionsAsync(Guid accountId);
    Task DeleteSessionAsync(Guid sessionId, Guid accountId);
    Task SaveChangesAsync();
}
