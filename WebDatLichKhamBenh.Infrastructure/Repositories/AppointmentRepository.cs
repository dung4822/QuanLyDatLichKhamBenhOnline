using Microsoft.EntityFrameworkCore;
using WebDatLichKhamBenh.Application.DTOs.Appointments;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Domain.Entities;
using WebDatLichKhamBenh.Domain.Enums;
using WebDatLichKhamBenh.Infrastructure.Persistence;

namespace WebDatLichKhamBenh.Infrastructure.Repositories;

public class AppointmentRepository : IAppointmentRepository
{
    private readonly AppDbContext _context;
    public AppointmentRepository(AppDbContext context) => _context = context;

    public Task<Appointment?> GetByIdAsync(int id, bool tracking = false)
    {
        // Không mất lịch sử khi bác sĩ/bệnh nhân đã xóa mềm.
        var query = _context.Appointments.IgnoreQueryFilters();
        return (tracking ? query : query.AsNoTracking()).FirstOrDefaultAsync(x => x.AppointmentId == id);
    }

    public async Task<(List<Appointment> Items, int TotalCount)> GetAllAsync(AppointmentQuery filter)
    {
        var query = _context.Appointments.IgnoreQueryFilters().AsNoTracking();
        if (filter.PatientId.HasValue) query = query.Where(x => x.PatientId == filter.PatientId);
        if (filter.DoctorId.HasValue) query = query.Where(x => x.DoctorId == filter.DoctorId);
        if (filter.SpecialtyId.HasValue) query = query.Where(x => x.SpecialtyId == filter.SpecialtyId);
        if (filter.FromDate.HasValue) query = query.Where(x => x.Date >= filter.FromDate);
        if (filter.ToDate.HasValue) query = query.Where(x => x.Date <= filter.ToDate);
        if (filter.Status.HasValue) query = query.Where(x => x.Status == filter.Status);
        var total = await query.CountAsync();
        var items = await query.OrderBy(x => x.Date).ThenBy(x => x.StartTime).ThenBy(x => x.AppointmentId)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToListAsync();
        return (items, total);
    }

    public Task<AppointmentSlot?> GetSlotAsync(int slotId)
        => _context.AppointmentSlots.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.Doctor).ThenInclude(x => x.Specialty)
            .Include(x => x.DoctorShift).ThenInclude(x => x.Shift)
            .FirstOrDefaultAsync(x => x.AppointmentSlotId == slotId);

    public Task<bool> HasDoctorOverlapAsync(int doctorId, DateOnly date, TimeOnly startTime, TimeOnly endTime, int? excludeAppointmentId = null)
        => _context.Appointments.IgnoreQueryFilters().AnyAsync(x => x.DoctorId == doctorId &&
            (!excludeAppointmentId.HasValue || x.AppointmentId != excludeAppointmentId.Value) &&
            x.Status != AppointmentStatus.Cancelled && x.Date == date &&
            x.StartTime < endTime && startTime < x.EndTime);

    public Task<bool> HasPatientOverlapAsync(int patientId, DateOnly date, TimeOnly startTime, TimeOnly endTime, int? excludeAppointmentId = null)
        => _context.Appointments.IgnoreQueryFilters().AnyAsync(x => x.PatientId == patientId &&
            (!excludeAppointmentId.HasValue || x.AppointmentId != excludeAppointmentId.Value) &&
            x.Status != AppointmentStatus.Cancelled && x.Date == date &&
            x.StartTime < endTime && startTime < x.EndTime);

    public Task<bool> HasUpcomingConfirmedAsync(DateTime clinicNow, int? doctorId = null,
        int? patientId = null, int? specialtyId = null, int? shiftId = null,
        int[]? doctorShiftIds = null, DateOnly? date = null)
    {
        var today = DateOnly.FromDateTime(clinicNow);
        var time = TimeOnly.FromDateTime(clinicNow);
        var query = _context.Appointments.IgnoreQueryFilters().Where(x =>
            x.Status == AppointmentStatus.Confirmed &&
            (x.Date > today || (x.Date == today && x.EndTime > time)));
        if (doctorId.HasValue) query = query.Where(x => x.DoctorId == doctorId);
        if (patientId.HasValue) query = query.Where(x => x.PatientId == patientId);
        if (specialtyId.HasValue) query = query.Where(x => x.SpecialtyId == specialtyId);
        if (shiftId.HasValue) query = query.Where(x => x.AppointmentSlot.DoctorShift.ShiftId == shiftId);
        if (doctorShiftIds is not null) query = query.Where(x => doctorShiftIds.Contains(x.AppointmentSlot.DoctorShiftId));
        if (date.HasValue) query = query.Where(x => x.Date == date);
        return query.AnyAsync();
    }

    public async Task<bool> TryBookSlotAsync(int slotId)
    {
        var changedAt = DateTime.UtcNow;
        var updated = await _context.AppointmentSlots
            .Where(x => x.AppointmentSlotId == slotId && x.Status == AppointmentSlotStatus.Available)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, AppointmentSlotStatus.Booked)
                .SetProperty(x => x.UnavailableReason, (string?)null)
                .SetProperty(x => x.UpdatedAt, changedAt));
        return updated == 1;
    }

    public async Task ReleaseSlotAsync(int slotId, string? unavailableReason)
    {
        var changedAt = DateTime.UtcNow;
        var status = unavailableReason is null ? AppointmentSlotStatus.Available : AppointmentSlotStatus.Unavailable;
        await _context.AppointmentSlots.Where(x => x.AppointmentSlotId == slotId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, status)
                .SetProperty(x => x.UnavailableReason, unavailableReason).SetProperty(x => x.UpdatedAt, changedAt));
    }

    public async Task AddAsync(Appointment appointment) => await _context.Appointments.AddAsync(appointment);
    public async Task AddHistoryAsync(AppointmentHistory history) => await _context.AppointmentHistories.AddAsync(history);
    public Task<List<AppointmentHistory>> GetHistoryAsync(int appointmentId, int page, int pageSize)
        => _context.AppointmentHistories.AsNoTracking().Where(x => x.AppointmentId == appointmentId)
            .OrderByDescending(x => x.AppointmentHistoryId).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
    public Task SaveChangesAsync() => _context.SaveChangesAsync();
}
