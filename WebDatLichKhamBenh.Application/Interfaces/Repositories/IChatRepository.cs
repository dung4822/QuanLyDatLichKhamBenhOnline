using WebDatLichKhamBenh.Domain.Entities;
using WebDatLichKhamBenh.Domain.Enums;

namespace WebDatLichKhamBenh.Application.Interfaces.Repositories;

public interface IChatRepository
{
    Task AddSessionAsync(ChatSession session);

    // Chỉ trả cuộc trò chuyện thuộc đúng bệnh nhân này.
    Task<ChatSession?> GetSessionAsync(Guid sessionId, int patientId, bool tracking = false);
    Task<List<ChatSession>> GetSessionsAsync(int patientId, int take);

    // Các lượt gần nhất (kèm đề xuất), sắp xếp cũ → mới.
    Task<List<ChatTurn>> GetRecentTurnsAsync(Guid sessionId, int take);
    Task<ChatTurn?> GetTurnByClientMessageIdAsync(Guid sessionId, Guid clientMessageId);

    Task<ChatAction?> GetActionAsync(Guid actionId, int patientId, bool tracking = false);

    // Chuyển Pending → trạng thái mới trong MỘT câu UPDATE có điều kiện; false nếu đề xuất không còn Pending.
    // Nhờ vậy hai request (bấm Xác nhận hai lần, vừa Xác nhận vừa Hủy) không ghi đè kết quả của nhau.
    Task<bool> TryCompleteAsync(Guid actionId, ChatActionStatus status, string? failureReason = null);

    // Các đề xuất cũ còn Pending trong cuộc trò chuyện chuyển thành Superseded, trừ đề xuất mới nhất.
    Task SupersedePendingAsync(Guid sessionId, Guid keepActionId);

    // Lưu lượt chat mới cùng các thay đổi đang theo dõi. Trả false nếu ClientMessageId đã được lưu
    // bởi một request khác chạy song song.
    Task<bool> TryAddTurnAsync(ChatTurn turn);
    Task SaveChangesAsync();
}
