using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Infrastructure.Persistence.Configurations;

public class ChatActionConfiguration : IEntityTypeConfiguration<ChatAction>
{
    public void Configure(EntityTypeBuilder<ChatAction> builder)
    {
        builder.ToTable("ChatActions", table =>
        {
            table.HasCheckConstraint("CK_ChatActions_Type", "\"Type\" IN (0, 1, 2, 3)");
            table.HasCheckConstraint("CK_ChatActions_Status", "\"Status\" IN (0, 1, 2, 3, 4, 5)");
        });
        builder.HasKey(x => x.ChatActionId);
        builder.Property(x => x.Type).HasConversion<int>();
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.PreviewJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ResultJson).HasColumnType("jsonb");
        builder.Property(x => x.FailureReason).HasMaxLength(500);
        // Mỗi lượt chat có tối đa một đề xuất.
        builder.HasIndex(x => x.ChatTurnId).IsUnique();
        builder.HasOne(x => x.ChatTurn).WithOne(x => x.Action)
            .HasForeignKey<ChatAction>(x => x.ChatTurnId).OnDelete(DeleteBehavior.Cascade);
    }
}
