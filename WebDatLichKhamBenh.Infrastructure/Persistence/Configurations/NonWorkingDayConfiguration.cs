using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Infrastructure.Persistence.Configurations;

public class NonWorkingDayConfiguration : IEntityTypeConfiguration<NonWorkingDay>
{
    public void Configure(EntityTypeBuilder<NonWorkingDay> builder)
    {
        builder.ToTable("NonWorkingDays");

        builder.HasKey(nonWorkingDay => nonWorkingDay.NonWorkingDayId);

        builder.Property(nonWorkingDay => nonWorkingDay.Date)
            .IsRequired();

        builder.Property(nonWorkingDay => nonWorkingDay.Reason)
            .HasMaxLength(500);

        builder.Property(nonWorkingDay => nonWorkingDay.CreatedAt)
            .IsRequired();

        builder.Property(nonWorkingDay => nonWorkingDay.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        builder.HasOne(nonWorkingDay => nonWorkingDay.Doctor)
            .WithMany(doctor => doctor.NonWorkingDays)
            .HasForeignKey(nonWorkingDay => nonWorkingDay.DoctorId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // Mỗi bác sĩ chỉ có một lịch nghỉ còn hiệu lực cho cùng một ngày.
        builder.HasIndex(nonWorkingDay => new { nonWorkingDay.DoctorId, nonWorkingDay.Date })
            .IsUnique()
            .HasFilter("\"DoctorId\" IS NOT NULL AND \"IsDeleted\" = false");

        // Cả bệnh viện cũng chỉ có một thông báo nghỉ còn hiệu lực trong một ngày.
        builder.HasIndex(nonWorkingDay => nonWorkingDay.Date)
            .IsUnique()
            .HasFilter("\"DoctorId\" IS NULL AND \"IsDeleted\" = false");

        builder.HasQueryFilter(nonWorkingDay => !nonWorkingDay.IsDeleted);
    }
}
