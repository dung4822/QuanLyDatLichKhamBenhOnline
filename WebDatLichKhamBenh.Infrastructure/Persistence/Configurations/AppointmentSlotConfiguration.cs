using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Infrastructure.Persistence.Configurations;

public class AppointmentSlotConfiguration : IEntityTypeConfiguration<AppointmentSlot>
{
    public void Configure(EntityTypeBuilder<AppointmentSlot> builder)
    {
        builder.ToTable("AppointmentSlots");
        builder.HasKey(slot => slot.AppointmentSlotId);

        builder.Property(slot => slot.Date).IsRequired();
        builder.Property(slot => slot.StartTime).HasColumnType("time without time zone").IsRequired();
        builder.Property(slot => slot.EndTime).HasColumnType("time without time zone").IsRequired();
        builder.Property(slot => slot.Status).HasConversion<int>().IsRequired();
        builder.Property(slot => slot.UnavailableReason).HasMaxLength(500);
        builder.Property(slot => slot.CreatedAt).IsRequired();

        builder.HasOne(slot => slot.Doctor)
            .WithMany(doctor => doctor.AppointmentSlots)
            .HasForeignKey(slot => slot.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(slot => slot.DoctorShift)
            .WithMany(doctorShift => doctorShift.AppointmentSlots)
            .HasForeignKey(slot => slot.DoctorShiftId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(slot => new { slot.DoctorId, slot.Date, slot.StartTime }).IsUnique();
        builder.HasIndex(slot => new { slot.DoctorId, slot.Date, slot.Status });
    }
}
