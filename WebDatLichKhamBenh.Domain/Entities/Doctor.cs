using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

    namespace WebDatLichKhamBenh.Domain.Entities
    {
        public class Doctor
        {
            public int DoctorId { get; set; }
            public string FullName { get; set; } = string.Empty;
            public string? PhoneNumber { get; set; }
            public string? Email { get; set; } = string.Empty;
            public int Gender { get; set; }
            public string? Address { get; set; }
        //true là đã xóa, false là chưa xóa
             public bool IsDelete { get; set; } = false;
            public DateOnly CareerStartDate { get; set; }
            public string? AvatarUrl { get; set; }
            public string? AvatarStorageKey { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime? UpdatedAt { get; set; }



            //nối với khóa ngoại là bảng specialty
             public int SpecialtyId { get; set; }
             public Specialty Specialty { get; set; } = null!;

             // Các ca làm việc lặp lại hằng tuần mà bác sĩ được phân công.
             public ICollection<DoctorShift> DoctorShifts { get; set; } = new List<DoctorShift>();

             // Các ngày nghỉ riêng của bác sĩ. DoctorId null ở NonWorkingDay nghĩa là cả bệnh viện nghỉ.
             public ICollection<NonWorkingDay> NonWorkingDays { get; set; } = new List<NonWorkingDay>();

             public ICollection<AppointmentSlot> AppointmentSlots { get; set; } = new List<AppointmentSlot>();

    }
}
