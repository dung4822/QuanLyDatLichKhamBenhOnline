using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebDatLichKhamBenh.Application.DTOs.Images;

namespace WebDatLichKhamBenh.Application.DTOs.Doctors
{
    public record UpdateDoctorRequest
    {
        public string FullName { get; init; } = default!;
        public string? PhoneNumber { get; init; }
        public string? Email { get; init; }
        public int Gender { get; init; }
        public string? Address { get; init; }
        public DateOnly CareerStartDate { get; init; }
        public int SpecialtyId { get; init; }
        public ImageUploadRequest? Avatar { get; init; }
        public bool RemoveAvatar { get; init; }
    }
}
