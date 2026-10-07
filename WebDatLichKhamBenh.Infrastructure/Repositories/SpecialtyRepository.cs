using Microsoft.EntityFrameworkCore;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;
using WebDatLichKhamBenh.Domain.Entities;
using WebDatLichKhamBenh.Infrastructure.Persistence;

namespace WebDatLichKhamBenh.Infrastructure.Repositories;

public class SpecialtyRepository : ISpecialtyRepository
{
    private readonly AppDbContext _context;

    public SpecialtyRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<Specialty>> GetAllAsync()
    {
        return await _context.Specialties
            .AsNoTracking()
            .OrderBy(x => x.SpecialtyId)
            .ToListAsync();
    }

    public async Task AddAsync(Specialty specialty)
    {
        await _context.Specialties.AddAsync(specialty);
    }

    public async Task<Specialty?> GetByIdAsync(int specialtyId)
    {
        return await _context.Specialties.AsNoTracking().FirstOrDefaultAsync(x => x.SpecialtyId == specialtyId);
    }

    public async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
    }

    public async Task<Specialty?> GetByIdTrackingAsync(int specialtyId)
    {
        return await _context.Specialties.FirstOrDefaultAsync(x => x.SpecialtyId == specialtyId);
    }
}
