using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Infrastructure.Persistence.Configurations;

public class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.ToTable("Patients");

        builder.HasKey(patient => patient.PatientId);

        builder.Property(patient => patient.FullName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(patient => patient.PhoneNumber)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(patient => patient.Email)
            .HasMaxLength(50);

        builder.Property(patient => patient.Address)
            .HasMaxLength(255);

        builder.HasQueryFilter(patient => patient.DeletedAt == null);
    }
}
