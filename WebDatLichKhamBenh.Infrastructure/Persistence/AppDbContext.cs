using Microsoft.EntityFrameworkCore;
using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Specialty> Specialties { get; set; }
    public DbSet<Doctor> Doctors { get; set; }
    public DbSet<Patient> Patients { get; set; }
    public DbSet<Shift> Shifts { get; set; }
    public DbSet<DoctorShift> DoctorShifts { get; set; }
    public DbSet<NonWorkingDay> NonWorkingDays { get; set; }
    public DbSet<AppointmentSlot> AppointmentSlots { get; set; }
    public DbSet<Appointment> Appointments { get; set; }
    public DbSet<UserAccount> UserAccounts { get; set; }
    public DbSet<UserSession> UserSessions { get; set; }
    public DbSet<AppointmentHistory> AppointmentHistories { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
