using Microsoft.EntityFrameworkCore;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Domain.Entities;
using WebDatLichKhamBenh.Infrastructure.Persistence;

namespace WebDatLichKhamBenh.Infrastructure.Repositories;

public class ShiftRepository : IShiftRepository
{
    private readonly AppDbContext _context;

    public ShiftRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<Shift>> GetAllAsync()
    {
        return await _context.Shifts
            .AsNoTracking()
            .OrderBy(shift => shift.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)shift.DayOfWeek)
            .ThenBy(shift => shift.StartTime)
            .ToListAsync();
    }

    public async Task<List<Shift>> GetByIdsAsync(IReadOnlyCollection<int> shiftIds)
    {
        if (shiftIds.Count == 0)
        {
            return [];
        }

        return await _context.Shifts
            .AsNoTracking()
            .Where(shift => shiftIds.Contains(shift.ShiftId))
            .ToListAsync();
    }

    public async Task<Shift?> GetByIdAsync(int shiftId)
    {
        return await _context.Shifts
            .AsNoTracking()
            .FirstOrDefaultAsync(shift => shift.ShiftId == shiftId);
    }

    public async Task<Shift?> GetByIdTrackingAsync(int shiftId)
    {
        return await _context.Shifts
            .FirstOrDefaultAsync(shift => shift.ShiftId == shiftId);
    }

    public async Task AddAsync(Shift shift)
    {
        await _context.Shifts.AddAsync(shift);
    }

    public async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
    }
}
