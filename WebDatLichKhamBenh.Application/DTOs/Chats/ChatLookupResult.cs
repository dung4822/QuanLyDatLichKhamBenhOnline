using System.Text.Json;

namespace WebDatLichKhamBenh.Application.DTOs.Chats;

// Dữ liệu do backend tra cứu, tách khỏi câu trả lời do mô hình diễn đạt.
public record ChatLookupResult(string Tool, JsonElement Data);
