using WebDatLichKhamBenh.Application.DTOs.DoctorShifts;

namespace WebDatLichKhamBenh.Application.Interfaces.Services;

public interface IDoctorShiftService
{
    Task<List<DoctorShiftDto>?> GetWeeklyScheduleAsync(int doctorId);
    Task<List<DoctorShiftDto>?> ReplaceWeeklyScheduleAsync(
        int doctorId,
        ReplaceDoctorShiftsRequest replaceDoctorShiftsRequest);
}
