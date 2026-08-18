using Microsoft.EntityFrameworkCore;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Domain.Entities;
using WebDatLichKhamBenh.Infrastructure.Persistence;

namespace WebDatLichKhamBenh.Infrastructure.Repositories;

public class PatientRepository : IPatientRepository
{
    private readonly AppDbContext _context;

    public PatientRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<Patient>> GetAllAsync()
    {
        return await _context.Patients
            .AsNoTracking()
            .OrderBy(patient => patient.PatientId)
            .ToListAsync();
    }

    public async Task<Patient?> GetByIdAsync(int patientId)
    {
        return await _context.Patients
            .AsNoTracking()
            .FirstOrDefaultAsync(patient => patient.PatientId == patientId);
    }

    public async Task<Patient?> GetByIdTrackingAsync(int patientId)
    {
        return await _context.Patients
            .FirstOrDefaultAsync(patient => patient.PatientId == patientId);
    }

    public async Task AddAsync(Patient patient)
    {
        await _context.Patients.AddAsync(patient);
    }

    public async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
    }
}
