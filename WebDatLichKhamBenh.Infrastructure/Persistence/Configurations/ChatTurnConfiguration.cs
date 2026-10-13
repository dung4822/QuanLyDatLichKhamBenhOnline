using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Infrastructure.Persistence.Configurations;

public class ChatTurnConfiguration : IEntityTypeConfiguration<ChatTurn>
{
    public void Configure(EntityTypeBuilder<ChatTurn> builder)
    {
        builder.HasKey(x => x.ChatTurnId);
        builder.Property(x => x.UserMessage).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.AssistantReply).HasMaxLength(4000).IsRequired();
        // Chặn lưu trùng khi client gửi lại cùng một tin nhắn (retry sau khi mất mạng).
        builder.HasIndex(x => new { x.ChatSessionId, x.ClientMessageId }).IsUnique();
        builder.HasOne(x => x.ChatSession).WithMany(x => x.Turns)
            .HasForeignKey(x => x.ChatSessionId).OnDelete(DeleteBehavior.Cascade);
    }
}
