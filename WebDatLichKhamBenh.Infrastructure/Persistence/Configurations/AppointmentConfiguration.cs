using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Infrastructure.Persistence.Configurations;

public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("Appointments", table =>
        {
            table.HasCheckConstraint("CK_Appointments_Time", "\"EndTime\" > \"StartTime\"");
            table.HasCheckConstraint("CK_Appointments_Status", "\"Status\" IN (0, 1, 2, 3)");
        });
        builder.HasKey(x => x.AppointmentId);
        builder.Property(x => x.PatientFullName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.PatientPhoneNumber).HasMaxLength(20).IsRequired();
        builder.Property(x => x.DoctorFullName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.SpecialtyName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.Property(x => x.CancellationReason).HasMaxLength(500);
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.StartTime).HasColumnType("time without time zone");
        builder.Property(x => x.EndTime).HasColumnType("time without time zone");
        builder.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Doctor).WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Specialty).WithMany().HasForeignKey(x => x.SpecialtyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AppointmentSlot).WithMany().HasForeignKey(x => x.AppointmentSlotId).OnDelete(DeleteBehavior.Restrict);
        // Lịch đã hủy vẫn được giữ; chỉ hủy mới cho phép đặt lại cùng slot.
        builder.HasIndex(x => x.AppointmentSlotId).IsUnique().HasFilter("\"Status\" <> 1");
        builder.HasIndex(x => new { x.DoctorId, x.Date, x.StartTime });
        builder.HasIndex(x => new { x.PatientId, x.Date, x.StartTime });
        builder.HasIndex(x => new { x.Date, x.StartTime, x.AppointmentId });
    }
}
