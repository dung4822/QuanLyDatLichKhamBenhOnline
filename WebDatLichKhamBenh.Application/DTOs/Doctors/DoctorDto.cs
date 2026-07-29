using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WebDatLichKhamBenh.Application.DTOs.Doctors
{
    public record DoctorDto
    {
        public int DoctorId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }
        public int Gender { get; set; }
        public string? Address { get; set; }
        public string? AvatarUrl { get; set; }
        public DateOnly CareerStartDate { get; set; }
        public int Experience { get; set; }
    }
}
