using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Infrastructure.Persistence.Configurations;

public class AppointmentHistoryConfiguration : IEntityTypeConfiguration<AppointmentHistory>
{
    public void Configure(EntityTypeBuilder<AppointmentHistory> builder)
    {
        builder.ToTable("AppointmentHistories");
        builder.HasKey(x => x.AppointmentHistoryId);
        builder.Property(x => x.ActorDisplayName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ActorRole).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(30).IsRequired();
        builder.Property(x => x.BeforeJson).HasColumnType("jsonb");
        builder.Property(x => x.AfterJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(x => new { x.AppointmentId, x.AppointmentHistoryId });
        builder.HasOne(x => x.Appointment).WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ActorAccount).WithMany().HasForeignKey(x => x.ActorAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
