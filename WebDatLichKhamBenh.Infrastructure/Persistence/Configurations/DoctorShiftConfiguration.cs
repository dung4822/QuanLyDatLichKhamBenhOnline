using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Infrastructure.Persistence.Configurations;

public class DoctorShiftConfiguration : IEntityTypeConfiguration<DoctorShift>
{
    public void Configure(EntityTypeBuilder<DoctorShift> builder)
    {
        builder.ToTable("DoctorShifts");

        builder.HasKey(doctorShift => doctorShift.DoctorShiftId);

        builder.Property(doctorShift => doctorShift.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(doctorShift => doctorShift.CreatedAt)
            .IsRequired();

        builder.HasOne(doctorShift => doctorShift.Doctor)
            .WithMany(doctor => doctor.DoctorShifts)
            .HasForeignKey(doctorShift => doctorShift.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(doctorShift => doctorShift.Shift)
            .WithMany(shift => shift.DoctorShifts)
            .HasForeignKey(doctorShift => doctorShift.ShiftId)
            .OnDelete(DeleteBehavior.Restrict);

        // Một bác sĩ không thể được phân công cùng một ca hai lần khi cả hai bản ghi còn hiệu lực.
        // Sau khi xóa mềm, có thể tạo một bản ghi mới nếu bác sĩ quay lại làm ca đó.
        builder.HasIndex(doctorShift => new { doctorShift.DoctorId, doctorShift.ShiftId })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder.HasQueryFilter(doctorShift => !doctorShift.IsDeleted);
    }
}
