using Microsoft.EntityFrameworkCore;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Domain.Entities;
using WebDatLichKhamBenh.Domain.Enums;
using WebDatLichKhamBenh.Infrastructure.Persistence;

namespace WebDatLichKhamBenh.Infrastructure.Repositories;

public class AccountRepository : IAccountRepository
{
    private readonly AppDbContext _context;

    public AccountRepository(AppDbContext context)
    {
        _context = context;
    }

    // Giữ tracking vì service còn cập nhật mật khẩu, bộ đếm đăng nhập và quyền tài khoản.
    public Task<UserAccount?> GetByIdAsync(Guid id)
    {
        return _context.UserAccounts.FirstOrDefaultAsync(account => account.UserAccountId == id);
    }

    public Task<UserAccount?> GetByEmailAsync(string normalizedEmail)
    {
        return _context.UserAccounts.FirstOrDefaultAsync(account => account.NormalizedEmail == normalizedEmail);
    }

    public Task<List<UserAccount>> GetPageAsync(int page, int pageSize)
    {
        return _context.UserAccounts.AsNoTracking()
            .OrderBy(account => account.CreatedAt)
            .ThenBy(account => account.UserAccountId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public Task<bool> LinkExistsAsync(int? patientId, int? doctorId, Guid? excludeId = null)
    {
        return _context.UserAccounts.AnyAsync(account =>
            (!excludeId.HasValue || account.UserAccountId != excludeId) &&
            ((patientId.HasValue && account.PatientId == patientId) ||
             (doctorId.HasValue && account.DoctorId == doctorId)));
    }

    public Task<int> CountActiveAdminsAsync()
    {
        return _context.UserAccounts.CountAsync(account => account.Role == AccountRole.Admin && account.IsActive);
    }

    public async Task AddAsync(UserAccount account)
    {
        await _context.UserAccounts.AddAsync(account);
    }

    public async Task AddSessionAsync(UserSession session)
    {
        await _context.UserSessions.AddAsync(session);
    }

    public Task<UserSession?> FindSessionAsync(Guid sessionId, Guid accountId)
    {
        return _context.UserSessions.AsNoTracking()
            .Include(session => session.UserAccount)
            .FirstOrDefaultAsync(session => session.UserSessionId == sessionId && session.UserAccountId == accountId);
    }

    // ExecuteDeleteAsync chạy SQL ngay; không cần tải từng phiên vào bộ nhớ hay gọi SaveChanges.
    public async Task DeleteExpiredSessionsAsync(Guid accountId, DateTime now)
    {
        await _context.UserSessions
            .Where(session => session.UserAccountId == accountId && session.ExpiresAt <= now)
            .ExecuteDeleteAsync();
    }

    public async Task DeleteSessionsAsync(Guid accountId)
    {
        await _context.UserSessions.Where(session => session.UserAccountId == accountId).ExecuteDeleteAsync();
    }

    public async Task DeleteSessionAsync(Guid sessionId, Guid accountId)
    {
        await _context.UserSessions
            .Where(session => session.UserSessionId == sessionId && session.UserAccountId == accountId)
            .ExecuteDeleteAsync();
    }

    public Task SaveChangesAsync()
    {
        return _context.SaveChangesAsync();
    }
}
