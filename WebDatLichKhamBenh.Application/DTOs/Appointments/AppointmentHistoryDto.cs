using WebDatLichKhamBenh.Domain.Enums;

namespace WebDatLichKhamBenh.Application.DTOs.Appointments;

public record AppointmentSnapshot(int AppointmentSlotId, int DoctorId, string DoctorFullName,
    int SpecialtyId, string SpecialtyName, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime,
    AppointmentStatus Status, string? CancellationReason);

public record AppointmentHistoryDto(long AppointmentHistoryId, int AppointmentId,
    Guid ActorAccountId, string ActorDisplayName, string ActorRole, string Action,
    AppointmentSnapshot? Before, AppointmentSnapshot After, DateTime OccurredAt);
