using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Domain.Enums;

namespace WebDatLichKhamBenh.Application.Extensions;

// Các câu hỏi phân quyền hay dùng, viết một lần để mọi service kiểm tra giống nhau.
public static class CurrentUserExtensions
{
    public static bool IsStaff(this ICurrentUser user)
        => user.Role is AccountRole.Admin or AccountRole.Receptionist;

    // Người đang đăng nhập có phải chính bệnh nhân này không.
    public static bool IsPatient(this ICurrentUser user, int patientId)
        => user.Role == AccountRole.Patient && user.PatientId == patientId;

    // Người đang đăng nhập có phải chính bác sĩ này không.
    public static bool IsDoctor(this ICurrentUser user, int doctorId)
        => user.Role == AccountRole.Doctor && user.DoctorId == doctorId;
}
