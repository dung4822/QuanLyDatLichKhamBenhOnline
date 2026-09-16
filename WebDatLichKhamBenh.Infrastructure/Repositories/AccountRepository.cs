using Microsoft.EntityFrameworkCore;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Domain.Entities;
using WebDatLichKhamBenh.Domain.Enums;
using WebDatLichKhamBenh.Infrastructure.Persistence;

namespace WebDatLichKhamBenh.Infrastructure.Repositories;

public class AccountRepository(AppDbContext context) : IAccountRepository
{
    public Task<UserAccount?> GetByIdAsync(Guid id) => context.UserAccounts.FirstOrDefaultAsync(x => x.UserAccountId == id);
    public Task<UserAccount?> GetByEmailAsync(string normalizedEmail) => context.UserAccounts.FirstOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail);
    public Task<List<UserAccount>> GetPageAsync(int page, int pageSize) => context.UserAccounts.AsNoTracking()
        .OrderBy(x => x.CreatedAt).ThenBy(x => x.UserAccountId).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
    public Task<bool> LinkExistsAsync(int? patientId, int? doctorId, Guid? excludeId = null) => context.UserAccounts.AnyAsync(x =>
        (!excludeId.HasValue || x.UserAccountId != excludeId) &&
        ((patientId.HasValue && x.PatientId == patientId) || (doctorId.HasValue && x.DoctorId == doctorId)));
    public Task<int> CountActiveAdminsAsync() => context.UserAccounts.CountAsync(x => x.Role == AccountRole.Admin && x.IsActive);
    public async Task AddAsync(UserAccount account) => await context.UserAccounts.AddAsync(account);
    public async Task AddSessionAsync(UserSession session) => await context.UserSessions.AddAsync(session);
    public Task<UserSession?> FindSessionAsync(string tokenHash) => context.UserSessions.AsNoTracking()
        .Include(x => x.UserAccount).FirstOrDefaultAsync(x => x.TokenHash == tokenHash);
    public async Task DeleteSessionsAsync(Guid accountId) => await context.UserSessions.Where(x => x.UserAccountId == accountId).ExecuteDeleteAsync();
    public async Task DeleteSessionAsync(Guid sessionId, Guid accountId) => await context.UserSessions
        .Where(x => x.UserSessionId == sessionId && x.UserAccountId == accountId).ExecuteDeleteAsync();
    public Task SaveChangesAsync() => context.SaveChangesAsync();
}
