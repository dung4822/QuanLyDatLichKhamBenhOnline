using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using WebDatLichKhamBenh.Application.DTOs.Doctors;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Domain.Entities;
using WebDatLichKhamBenh.Infrastructure.Persistence;

namespace WebDatLichKhamBenh.Infrastructure.Repositories;

public class DoctorRepository : IDoctorRepository
{
    private readonly AppDbContext _context;

    public DoctorRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<DoctorDto>> GetListDoctorAsync(int? specialtyId = null)
    {
        var query = _context.Doctors.AsNoTracking();
        if (specialtyId.HasValue)
            query = query.Where(d => d.SpecialtyId == specialtyId.Value);

        return await query.Select(ToDto).ToListAsync();
    }

    public async Task<DoctorDto?> GetDoctorByIdAsync(int doctorId)
    {
        return await _context.Doctors.AsNoTracking()
            .Where(d => d.DoctorId == doctorId)
            .Select(ToDto)
            .FirstOrDefaultAsync();
    }

    public async Task<Doctor?> GetDoctorByIdTrackingAsync(int doctorId)
    {
        return await _context.Doctors.FirstOrDefaultAsync(d => d.DoctorId == doctorId);
    }

    public async Task AddAsync(Doctor doctor)
    {
        await _context.Doctors.AddAsync(doctor);
    }

    public async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
    }

    // Chiếu thẳng sang DTO trong câu SQL: chỉ lấy các cột cần và JOIN sẵn tên chuyên khoa.
    // Viết dạng Expression (không phải hàm thường) để EF Core dịch được sang SQL.
    private static readonly Expression<Func<Doctor, DoctorDto>> ToDto = d => new DoctorDto
    {
        DoctorId = d.DoctorId,
        SpecialtyId = d.SpecialtyId,
        SpecialtyName = d.Specialty.Name,
        FullName = d.FullName,
        PhoneNumber = d.PhoneNumber,
        Email = d.Email,
        Gender = d.Gender,
        Address = d.Address,
        AvatarUrl = d.AvatarUrl,
        CareerStartDate = d.CareerStartDate,
        Experience = DateTime.UtcNow.Year - d.CareerStartDate.Year
    };
}
