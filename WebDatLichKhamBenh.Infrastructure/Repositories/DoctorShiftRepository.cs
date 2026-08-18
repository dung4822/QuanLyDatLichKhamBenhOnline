using Microsoft.EntityFrameworkCore;
using WebDatLichKhamBenh.Application.DTOs.DoctorShifts;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Domain.Entities;
using WebDatLichKhamBenh.Infrastructure.Persistence;

namespace WebDatLichKhamBenh.Infrastructure.Repositories;

public class DoctorShiftRepository : IDoctorShiftRepository
{
    private readonly AppDbContext _context;

    public DoctorShiftRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<DoctorShiftDto>> GetByDoctorIdAsync(int doctorId)
    {
        return await _context.DoctorShifts

         .Where(ds =>
             ds.DoctorId == doctorId &&
             !ds.IsDeleted &&
             !ds.Shift.IsDeleted &&
             ds.Shift.IsActive)

         .OrderBy(ds =>
             ds.Shift.DayOfWeek == DayOfWeek.Sunday
                 ? 7
                 : (int)ds.Shift.DayOfWeek)

         .ThenBy(ds => ds.Shift.StartTime)

         .Select(ds => new DoctorShiftDto
         {
             DoctorShiftId = ds.DoctorShiftId,

             DoctorId = ds.DoctorId,
             DoctorFullName = ds.Doctor.FullName,

             ShiftId = ds.ShiftId,
             ShiftName = ds.Shift.Name,
             DayOfWeek = ds.Shift.DayOfWeek,
             StartTime = ds.Shift.StartTime,
             EndTime = ds.Shift.EndTime,

             CreatedAt = ds.CreatedAt,
             UpdatedAt = ds.UpdatedAt
         })

         .ToListAsync();

    }

    public async Task<List<DoctorShift>> GetByDoctorIdTrackingAsync(int doctorId)
    {
        return await _context.DoctorShifts
            .Where(doctorShift => doctorShift.DoctorId == doctorId)
            .ToListAsync();
    }

    public async Task AddAsync(DoctorShift doctorShift)
    {
        await _context.DoctorShifts.AddAsync(doctorShift);
    }

    public async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
    }

    private IQueryable<DoctorShift> QueryWithDetails()
    {
        return _context.DoctorShifts
            .Include(doctorShift => doctorShift.Doctor)
            .Include(doctorShift => doctorShift.Shift);
    }
}
