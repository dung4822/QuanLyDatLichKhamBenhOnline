using Microsoft.EntityFrameworkCore;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Domain.Entities;
using WebDatLichKhamBenh.Infrastructure.Persistence;

namespace WebDatLichKhamBenh.Infrastructure.Repositories;

public class NonWorkingDayRepository : INonWorkingDayRepository
{
    private readonly AppDbContext _context;

    public NonWorkingDayRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<NonWorkingDay>> GetAllAsync(DateOnly? fromDate)
    {
        var query = _context.NonWorkingDays
            .AsNoTracking()
            .Include(nonWorkingDay => nonWorkingDay.Doctor)
            .AsQueryable();

        if (fromDate.HasValue)
        {
            query = query.Where(nonWorkingDay => nonWorkingDay.Date >= fromDate.Value);
        }

        return await query
            .OrderBy(nonWorkingDay => nonWorkingDay.Date)
            .ThenBy(nonWorkingDay => nonWorkingDay.DoctorId == null ? 0 : 1)
            .ThenBy(nonWorkingDay => nonWorkingDay.Doctor!.FullName)
            .ToListAsync();
    }

    public async Task<NonWorkingDay?> GetByIdAsync(int nonWorkingDayId)
    {
        return await _context.NonWorkingDays
            .AsNoTracking()
            .Include(nonWorkingDay => nonWorkingDay.Doctor)
            .FirstOrDefaultAsync(nonWorkingDay => nonWorkingDay.NonWorkingDayId == nonWorkingDayId);
    }

    public async Task<NonWorkingDay?> GetByIdTrackingAsync(int nonWorkingDayId)
    {
        return await _context.NonWorkingDays
            .FirstOrDefaultAsync(nonWorkingDay => nonWorkingDay.NonWorkingDayId == nonWorkingDayId);
    }

    public Task<bool> ExistsAsync(DateOnly date, int? doctorId, int? excludedNonWorkingDayId = null)
    {
        return _context.NonWorkingDays.AnyAsync(nonWorkingDay =>
            nonWorkingDay.Date == date &&
            nonWorkingDay.DoctorId == doctorId &&
            (!excludedNonWorkingDayId.HasValue ||
             nonWorkingDay.NonWorkingDayId != excludedNonWorkingDayId.Value));
    }

    public Task<bool> IsDoctorUnavailableAsync(int doctorId, DateOnly date)
    {
        return _context.NonWorkingDays.AnyAsync(nonWorkingDay =>
            nonWorkingDay.Date == date &&
            (nonWorkingDay.DoctorId == null || nonWorkingDay.DoctorId == doctorId));
    }

    public async Task AddAsync(NonWorkingDay nonWorkingDay)
    {
        await _context.NonWorkingDays.AddAsync(nonWorkingDay);
    }

    public async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
    }
}
