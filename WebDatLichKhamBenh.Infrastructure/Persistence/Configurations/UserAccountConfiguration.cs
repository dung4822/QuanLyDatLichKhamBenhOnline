using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Infrastructure.Persistence.Configurations;

public class UserAccountConfiguration : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(EntityTypeBuilder<UserAccount> builder)
    {
        builder.ToTable("UserAccounts", table => table.HasCheckConstraint("CK_UserAccounts_RoleLink",
            "(\"Role\" = 0 AND \"PatientId\" IS NOT NULL AND \"DoctorId\" IS NULL) OR " +
            "(\"Role\" = 1 AND \"DoctorId\" IS NOT NULL AND \"PatientId\" IS NULL) OR " +
            "(\"Role\" IN (2,3) AND \"PatientId\" IS NULL AND \"DoctorId\" IS NULL)"));
        builder.HasKey(x => x.UserAccountId);
        builder.Property(x => x.Email).HasMaxLength(254).IsRequired();
        builder.Property(x => x.NormalizedEmail).HasMaxLength(254).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
        builder.HasIndex(x => x.NormalizedEmail).IsUnique();
        builder.HasIndex(x => x.PatientId).IsUnique();
        builder.HasIndex(x => x.DoctorId).IsUnique();
        builder.HasOne(x => x.Patient).WithOne().HasForeignKey<UserAccount>(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Doctor).WithOne().HasForeignKey<UserAccount>(x => x.DoctorId).OnDelete(DeleteBehavior.Restrict);
    }
}
