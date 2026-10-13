using WebDatLichKhamBenh.Domain.Entities;

namespace WebDatLichKhamBenh.Application.DTOs.Chats;

// Kết quả một công cụ của nhóm thao tác: dữ liệu trả cho mô hình, kèm đề xuất (chưa lưu) nếu có.
public record ChatToolResult(ChatLookupResult Result, ChatAction? Proposal = null);
