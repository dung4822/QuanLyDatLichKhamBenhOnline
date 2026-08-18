using Microsoft.EntityFrameworkCore;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Domain.Entities;
using WebDatLichKhamBenh.Domain.Enums;
using WebDatLichKhamBenh.Infrastructure.Persistence;

namespace WebDatLichKhamBenh.Infrastructure.Repositories;

public class AppointmentSlotRepository : IAppointmentSlotRepository
{
    private readonly AppDbContext _context;

    public AppointmentSlotRepository(AppDbContext context)
    {
        _context = context;
    }

    public Task<List<DoctorShift>> GetActiveDoctorShiftsAsync(int? doctorId = null)
    {
        var query = _context.DoctorShifts
            .AsNoTracking()
            .Include(doctorShift => doctorShift.Doctor)
            .Include(doctorShift => doctorShift.Shift)
            .Where(doctorShift => !doctorShift.IsDeleted &&
                                  !doctorShift.Doctor.IsDelete &&
                                  !doctorShift.Shift.IsDeleted &&
                                  doctorShift.Shift.IsActive);

        if (doctorId.HasValue)
        {
            query = query.Where(doctorShift => doctorShift.DoctorId == doctorId.Value);
        }

        return query.ToListAsync();
    }

    public Task<List<DoctorShift>> GetDoctorShiftsByShiftIdAsync(int shiftId)
    {
        return _context.DoctorShifts
            .AsNoTracking()
            .Include(doctorShift => doctorShift.Doctor)
            .Include(doctorShift => doctorShift.Shift)
            .Where(doctorShift => !doctorShift.IsDeleted &&
                                  !doctorShift.Doctor.IsDelete &&
                                  doctorShift.ShiftId == shiftId)
            .ToListAsync();
    }

    public Task<List<NonWorkingDay>> GetNonWorkingDaysAsync(DateOnly fromDate, DateOnly toDate)
    {
        return _context.NonWorkingDays
            .AsNoTracking()
            .Where(nonWorkingDay => !nonWorkingDay.IsDeleted &&
                                    nonWorkingDay.Date >= fromDate &&
                                    nonWorkingDay.Date <= toDate)
            .ToListAsync();
    }

    public Task<List<AppointmentSlot>> GetSlotsTrackingAsync(DateOnly fromDate, DateOnly toDate, int? doctorId = null)
    {
        var query = _context.AppointmentSlots
            .Where(slot => slot.Date >= fromDate && slot.Date <= toDate);

        if (doctorId.HasValue)
        {
            query = query.Where(slot => slot.DoctorId == doctorId.Value);
        }

        return query.ToListAsync();
    }

    public Task<List<AppointmentSlot>> GetAvailableSlotsAsync(int doctorId, DateOnly fromDate, DateOnly toDate)
    {
        return _context.AppointmentSlots
            .AsNoTracking()
            .Where(slot => slot.DoctorId == doctorId &&
                           slot.Date >= fromDate &&
                           slot.Date <= toDate &&
                           slot.Status == AppointmentSlotStatus.Available)
            .OrderBy(slot => slot.Date)
            .ThenBy(slot => slot.StartTime)
            .ToListAsync();
    }

    public async Task AddRangeAsync(IEnumerable<AppointmentSlot> appointmentSlots)
    {
        await _context.AppointmentSlots.AddRangeAsync(appointmentSlots);
    }

    public Task SaveChangesAsync()
    {
        return _context.SaveChangesAsync();
    }
}
