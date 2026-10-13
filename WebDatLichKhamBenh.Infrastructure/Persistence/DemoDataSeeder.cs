using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebDatLichKhamBenh.Application.DTOs.Appointments;
using WebDatLichKhamBenh.Application.Extensions;
using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Application.Time;
using WebDatLichKhamBenh.Domain.Entities;
using WebDatLichKhamBenh.Domain.Enums;

namespace WebDatLichKhamBenh.Infrastructure.Persistence;

// Tạo dữ liệu mẫu cho bản demo (chạy bằng `dotnet run -- --seed-demo`).
// Chỉ chạy trên database chưa có chuyên khoa/bác sĩ nào, để không trộn với dữ liệu thật.
// Slot khám không tạo tay: gọi đúng hàm sinh slot của app để dữ liệu giống hệt khi dùng thật.
public class DemoDataSeeder
{
    public const string ReceptionistEmail = "letan@demo.com";
    public const string PatientEmail = "benhnhan@demo.com";
    public const string DoctorEmail = "bacsi@demo.com";

    private readonly AppDbContext _context;
    private readonly IPasswordService _passwords;
    private readonly IAppointmentSlotService _slotService;

    public DemoDataSeeder(AppDbContext context, IPasswordService passwords, IAppointmentSlotService slotService)
    {
        _context = context;
        _passwords = passwords;
        _slotService = slotService;
    }

    public async Task SeedAsync(string demoPassword)
    {
        if (demoPassword.Length < 12)
            throw new InvalidOperationException("Mật khẩu demo phải có ít nhất 12 ký tự.");
        if (await _context.Specialties.AnyAsync() || await _context.Doctors.IgnoreQueryFilters().AnyAsync())
            throw new InvalidOperationException("Database đã có dữ liệu; lệnh seed-demo chỉ dùng cho database trống.");

        var now = DateTime.UtcNow;
        var today = ClinicClock.Today;

        // 1. Chuyên khoa
        var specialties = new[]
        {
            NewSpecialty("Nội tổng quát", "Khám và điều trị các bệnh lý nội khoa thường gặp."),
            NewSpecialty("Nhi khoa", "Chăm sóc sức khỏe trẻ em từ sơ sinh đến 15 tuổi."),
            NewSpecialty("Da liễu", "Điều trị mụn, viêm da, dị ứng và các bệnh về da."),
            NewSpecialty("Tai Mũi Họng", "Khám viêm xoang, viêm họng, viêm tai."),
            NewSpecialty("Tim mạch", "Tầm soát và theo dõi tăng huyết áp, bệnh mạch vành.")
        };
        _context.Specialties.AddRange(specialties);

        // 2. Bác sĩ (Gender: 0 = nữ, 1 = nam)
        var doctors = new[]
        {
            NewDoctor("BS. Nguyễn Văn An", DoctorEmail, "0901000001", 1, 2010, specialties[0]),
            NewDoctor("BS. Trần Thị Bình", "binh.tran@demo.com", "0901000002", 0, 2014, specialties[1]),
            NewDoctor("BS. Lê Hoàng Cường", "cuong.le@demo.com", "0901000003", 1, 2016, specialties[2]),
            NewDoctor("BS. Phạm Thu Dung", "dung.pham@demo.com", "0901000004", 0, 2012, specialties[3]),
            NewDoctor("BS. Võ Minh Đức", "duc.vo@demo.com", "0901000005", 1, 2008, specialties[4]),
            NewDoctor("BS. Hoàng Ngọc Hà", "ha.hoang@demo.com", "0901000006", 0, 2018, specialties[0])
        };
        _context.Doctors.AddRange(doctors);

        // 3. Ca làm việc: sáng và chiều, từ thứ 2 đến thứ 7
        var workDays = new[]
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
            DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday
        };
        var shifts = new List<Shift>();
        foreach (var day in workDays)
        {
            shifts.Add(NewShift(day, "Ca sáng", new TimeOnly(7, 30), new TimeOnly(11, 30)));
            shifts.Add(NewShift(day, "Ca chiều", new TimeOnly(13, 30), new TimeOnly(17, 0)));
        }
        _context.Shifts.AddRange(shifts);

        // 4. Phân ca: bác sĩ chẵn làm sáng T2/T4/T6 + chiều T3/T5/T7, bác sĩ lẻ thì ngược lại.
        var doctorShifts = new List<DoctorShift>();
        for (var i = 0; i < doctors.Length; i++)
        {
            for (var d = 0; d < workDays.Length; d++)
            {
                var morning = (d + i) % 2 == 0;
                var shift = shifts.Single(s => s.DayOfWeek == workDays[d] && s.Name == (morning ? "Ca sáng" : "Ca chiều"));
                doctorShifts.Add(new DoctorShift { Doctor = doctors[i], Shift = shift, CreatedAt = now });
            }
        }
        _context.DoctorShifts.AddRange(doctorShifts);

        // 5. Ngày nghỉ: bác sĩ Cường nghỉ phép 1 ngày trong tuần tới
        _context.NonWorkingDays.Add(new NonWorkingDay
        {
            Doctor = doctors[2], Date = today.AddDays(3), Reason = "Nghỉ phép", CreatedAt = now
        });

        // 6. Bệnh nhân (Gender: 0 = nữ, 1 = nam)
        var patients = new[]
        {
            NewPatient("Nguyễn Thị Lan", new DateOnly(1995, 4, 12), 0, "0912000001", PatientEmail, "12 Lê Lợi, Quận 1, TP.HCM"),
            NewPatient("Trần Văn Minh", new DateOnly(1988, 9, 3), 1, "0912000002", "minh.tran@demo.com", "45 Nguyễn Huệ, Quận 1, TP.HCM"),
            NewPatient("Lê Thị Ngọc", new DateOnly(2001, 1, 25), 0, "0912000003", null, "8 Cách Mạng Tháng 8, Quận 3, TP.HCM"),
            NewPatient("Phạm Gia Bảo", new DateOnly(2018, 6, 30), 1, "0912000004", null, "102 Võ Văn Tần, Quận 3, TP.HCM"),
            NewPatient("Đỗ Văn Phúc", new DateOnly(1970, 11, 8), 1, "0912000005", null, "27 Hai Bà Trưng, Quận 1, TP.HCM")
        };
        _context.Patients.AddRange(patients);

        // 7. Tài khoản demo (dùng chung một mật khẩu)
        var receptionist = NewAccount(ReceptionistEmail, "Lễ tân Mai", AccountRole.Receptionist, demoPassword);
        var doctorAccount = NewAccount(DoctorEmail, doctors[0].FullName, AccountRole.Doctor, demoPassword);
        doctorAccount.Doctor = doctors[0];
        var patientAccount = NewAccount(PatientEmail, patients[0].FullName, AccountRole.Patient, demoPassword);
        patientAccount.Patient = patients[0];
        _context.UserAccounts.AddRange(receptionist, doctorAccount, patientAccount);

        await _context.SaveChangesAsync();

        // 8. Sinh slot 7 ngày tới bằng logic thật của app (bỏ qua ngày nghỉ ở bước 5).
        await _slotService.EnsureRollingWindowAsync();

        // 9. Lịch hẹn sắp tới: đặt vào các slot còn trống từ ngày mai.
        var freeSlots = await _context.AppointmentSlots
            .Where(s => s.Status == AppointmentSlotStatus.Available && s.Date > today)
            .OrderBy(s => s.Date).ThenBy(s => s.StartTime)
            .ToListAsync();
        var upcoming = new (Patient Patient, Doctor Doctor, string Reason, UserAccount Actor)[]
        {
            (patients[0], doctors[0], "Sốt nhẹ, ho khan 3 ngày", patientAccount),
            (patients[0], doctors[2], "Nổi mẩn đỏ ở tay", patientAccount),
            (patients[1], doctors[4], "Kiểm tra huyết áp định kỳ", receptionist),
            (patients[2], doctors[3], "Đau họng, khó nuốt", receptionist),
            (patients[3], doctors[1], "Bé biếng ăn, sụt cân", receptionist),
            (patients[4], doctors[0], "Đau đầu, chóng mặt", receptionist),
            (patients[1], doctors[5], "Khám sức khỏe tổng quát", receptionist)
        };
        foreach (var item in upcoming)
        {
            var slot = freeSlots.FirstOrDefault(s => s.DoctorId == item.Doctor.DoctorId);
            if (slot is null)
                continue;
            freeSlots.Remove(slot);
            slot.Status = AppointmentSlotStatus.Booked;
            slot.UpdatedAt = now;
            AddAppointment(item.Patient, item.Doctor, slot, item.Reason, AppointmentStatus.Confirmed, item.Actor);
        }

        // Một lịch đã hủy: slot được trả lại trạng thái trống như khi hủy thật.
        var cancelSlot = freeSlots.FirstOrDefault(s => s.DoctorId == doctors[1].DoctorId);
        if (cancelSlot is not null)
        {
            var cancelled = AddAppointment(patients[0], doctors[1], cancelSlot, "Tư vấn dinh dưỡng",
                AppointmentStatus.Cancelled, patientAccount);
            cancelled.CancelledAt = now;
            cancelled.CancellationReason = "Bận việc đột xuất";
        }

        // 10. Lịch đã khám trong tuần trước: tạo slot quá khứ theo đúng ca của bác sĩ.
        var past = new (Patient Patient, Doctor Doctor, string Reason, AppointmentStatus Status)[]
        {
            (patients[0], doctors[0], "Viêm họng", AppointmentStatus.Completed),
            (patients[1], doctors[4], "Đau tức ngực", AppointmentStatus.Completed),
            (patients[2], doctors[2], "Mụn trứng cá", AppointmentStatus.Completed),
            (patients[3], doctors[1], "Sốt cao", AppointmentStatus.Completed),
            (patients[4], doctors[3], "Ù tai", AppointmentStatus.NoShow)
        };
        var pastSlots = past.Select(item => CreatePastSlot(item.Doctor, doctorShifts, today, now)).ToList();
        // Lưu trước để slot có Id thật, lịch sử lịch hẹn ghi đúng AppointmentSlotId.
        await _context.SaveChangesAsync();
        for (var i = 0; i < past.Length; i++)
        {
            var (item, slot) = (past[i], pastSlots[i]);
            if (slot is null)
                continue;
            var actor = item.Patient == patients[0] ? patientAccount : receptionist;
            var appointment = AddAppointment(item.Patient, item.Doctor, slot, item.Reason, AppointmentStatus.Confirmed, actor);
            var before = Snapshot(appointment);
            appointment.Status = item.Status;
            appointment.UpdatedAt = now;
            AddHistory(appointment, item.Status.ToString(), before,
                item.Doctor == doctors[0] ? doctorAccount : receptionist);
        }

        await _context.SaveChangesAsync();
    }

    // Lấy ngày gần nhất trong 7 ngày qua mà bác sĩ có ca, tạo slot đầu ca ở ngày đó.
    private AppointmentSlot? CreatePastSlot(Doctor doctor, List<DoctorShift> doctorShifts, DateOnly today, DateTime now)
    {
        for (var daysAgo = 1; daysAgo <= 7; daysAgo++)
        {
            var date = today.AddDays(-daysAgo);
            var doctorShift = doctorShifts.FirstOrDefault(ds => ds.Doctor == doctor && ds.Shift.DayOfWeek == date.DayOfWeek);
            if (doctorShift is null)
                continue;
            var start = doctorShift.Shift.StartTime.AddMinutes(ClinicSchedule.SlotMinutes);
            var slot = new AppointmentSlot
            {
                Doctor = doctor, DoctorShift = doctorShift, Date = date,
                StartTime = start, EndTime = start.AddMinutes(ClinicSchedule.SlotMinutes),
                Status = AppointmentSlotStatus.Booked, CreatedAt = now
            };
            _context.AppointmentSlots.Add(slot);
            return slot;
        }
        return null;
    }

    private Appointment AddAppointment(Patient patient, Doctor doctor, AppointmentSlot slot, string reason,
        AppointmentStatus status, UserAccount actor)
    {
        var appointment = new Appointment
        {
            Patient = patient, PatientFullName = patient.FullName, PatientPhoneNumber = patient.PhoneNumber,
            AppointmentSlot = slot, Doctor = doctor, DoctorFullName = doctor.FullName,
            Specialty = doctor.Specialty, SpecialtyName = doctor.Specialty.Name,
            Date = slot.Date, StartTime = slot.StartTime, EndTime = slot.EndTime,
            Reason = reason, Status = status, CreatedAt = DateTime.UtcNow
        };
        _context.Appointments.Add(appointment);
        AddHistory(appointment, "Created", before: null, actor);
        return appointment;
    }

    private void AddHistory(Appointment appointment, string action, AppointmentSnapshot? before, UserAccount actor)
        => _context.AppointmentHistories.Add(new AppointmentHistory
        {
            Appointment = appointment,
            ActorAccount = actor,
            ActorDisplayName = actor.DisplayName,
            ActorRole = actor.Role.ToString(),
            Action = action,
            BeforeJson = before is null ? null : JsonSerializer.Serialize(before),
            AfterJson = JsonSerializer.Serialize(Snapshot(appointment)),
            OccurredAt = DateTime.UtcNow
        });

    private static AppointmentSnapshot Snapshot(Appointment x) => new(x.AppointmentSlot.AppointmentSlotId,
        x.Doctor.DoctorId, x.DoctorFullName, x.Specialty.SpecialtyId, x.SpecialtyName, x.Date, x.StartTime,
        x.EndTime, x.Status, x.CancellationReason);

    private static Specialty NewSpecialty(string name, string description) => new()
    {
        Name = name, Description = description, Status = SpecialtyStatus.Active, CreatedAt = DateTime.UtcNow
    };

    private static Doctor NewDoctor(string name, string email, string phone, int gender, int careerStartYear,
        Specialty specialty) => new()
    {
        FullName = name, Email = email, PhoneNumber = phone, Gender = gender,
        CareerStartDate = new DateOnly(careerStartYear, 1, 1), Specialty = specialty, CreatedAt = DateTime.UtcNow
    };

    private static Shift NewShift(DayOfWeek day, string name, TimeOnly start, TimeOnly end) => new()
    {
        DayOfWeek = day, Name = name, StartTime = start, EndTime = end, CreatedAt = DateTime.UtcNow
    };

    private static Patient NewPatient(string name, DateOnly dateOfBirth, int gender, string phone, string? email,
        string address) => new()
    {
        FullName = name, DateOfBirth = dateOfBirth, Gender = gender, PhoneNumber = phone,
        Email = email, Address = address, CreatedAt = DateTime.UtcNow
    };

    private UserAccount NewAccount(string email, string displayName, AccountRole role, string password)
    {
        var account = new UserAccount
        {
            Email = email, NormalizedEmail = email.ToNormalizedEmail(), DisplayName = displayName, Role = role
        };
        account.PasswordHash = _passwords.Hash(account, password);
        return account;
    }
}
