using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebDatLichKhamBenh.Application.DTOs.Doctors;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Domain.Entities;
using WebDatLichKhamBenh.Infrastructure.Persistence;

namespace WebDatLichKhamBenh.Infrastructure.Repositories
{
    public class DoctorRepository : IDoctorRepository
    {
        private readonly AppDbContext _context;
        public DoctorRepository(AppDbContext context)
        {
            _context = context;
        }
        public async Task AddAsync(Doctor doctor)
        {
            await _context.Doctors.AddAsync(doctor);
        }

        public async Task<DoctorDto?> GetDoctorByIdAsync(int doctorId)
        {
            return await _context.Doctors.
                Where(d => d.DoctorId == doctorId)
                .Select(d => new DoctorDto
                {
                    DoctorId = d.DoctorId,
                    FullName = d.FullName,
                    PhoneNumber = d.PhoneNumber,
                    Email = d.Email,
                    Gender = d.Gender,
                    Address = d.Address,
                    AvatarUrl = d.AvatarUrl,
                    CareerStartDate = d.CareerStartDate,
                    Experience = DateTime.UtcNow.Year - d.CareerStartDate.Year
                }).FirstOrDefaultAsync();
        }

        public async Task<Doctor?> GetDoctorByIdTrackingAsync(int doctorId)
        {
            return await _context.Doctors.FirstOrDefaultAsync(d => d.DoctorId == doctorId);
        }

        public async Task<List<DoctorDto>> GetListDoctorAsync()
        {
            return await _context.Doctors
                .Select(d => new DoctorDto
                {
                    DoctorId = d.DoctorId,
                    FullName = d.FullName,
                    PhoneNumber = d.PhoneNumber,
                    Email = d.Email,
                    Gender = d.Gender,
                    Address = d.Address,
                    AvatarUrl = d.AvatarUrl,
                    CareerStartDate = d.CareerStartDate,
                    Experience = DateTime.UtcNow.Year - d.CareerStartDate.Year
                }).ToListAsync();
        }

        public void Remove(Doctor doctor)
        {
            _context.Doctors.Remove(doctor);
        }

        public async Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync();
        }

        public void Update(Doctor doctor)
        {
            _context.Doctors.Update(doctor);
        }
    }
}
