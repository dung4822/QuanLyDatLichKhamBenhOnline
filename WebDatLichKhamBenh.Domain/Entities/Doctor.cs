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

    }
}
