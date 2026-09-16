using WebDatLichKhamBenh.Domain.Enums;

namespace WebDatLichKhamBenh.Application.Interfaces.Services;

public interface ICurrentUser
{
    Guid AccountId { get; }
    Guid SessionId { get; }
    string DisplayName { get; }
    AccountRole Role { get; }
    int? PatientId { get; }
    int? DoctorId { get; }
}
