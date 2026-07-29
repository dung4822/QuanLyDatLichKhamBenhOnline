using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Infrastructure.Persistence.Configurations
{
    public class DoctorConfiguration : IEntityTypeConfiguration<Doctor>
    {
        public void Configure(EntityTypeBuilder<Doctor> builder)
        {
            builder.ToTable("Doctors");

            builder.Property(x => x.FullName)
                .HasMaxLength(100)
                .IsRequired();
            builder.Property(x => x.PhoneNumber)
                .HasMaxLength(20);
            builder.Property(x => x.Email)
                .HasMaxLength(50);
            builder.HasIndex(x => x.Email).IsUnique();
            builder.Property(x => x.AvatarUrl).HasMaxLength(500);
            builder.Property(x => x.AvatarStorageKey).HasMaxLength(300);

            // Tự động loại bỏ các bác sĩ đã soft delete khỏi mọi truy vấn thông thường.
            builder.HasQueryFilter(d => !d.IsDelete);

            //Specialty - Doctor: 1 - N
            builder.HasOne(d => d.Specialty)
                   .WithMany(s => s.Doctors)
                   .HasForeignKey(d => d.SpecialtyId)
                   .OnDelete(DeleteBehavior.Restrict);


        }
    }
}
