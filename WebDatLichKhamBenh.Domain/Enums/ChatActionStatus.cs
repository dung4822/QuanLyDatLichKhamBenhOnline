namespace WebDatLichKhamBenh.Domain.Enums;

public enum ChatActionStatus
{
    // Đang chờ người dùng bấm Xác nhận hoặc Hủy.
    Pending = 0,
    // Người dùng đã xác nhận và thao tác đã thực hiện thành công.
    Confirmed = 1,
    // Người dùng bấm Hủy.
    Rejected = 2,
    // Quá thời hạn mà người dùng chưa xác nhận.
    Expired = 3,
    // Bị thay bằng một đề xuất mới hơn trong cùng cuộc trò chuyện.
    Superseded = 4,
    // Người dùng đã xác nhận nhưng nghiệp vụ từ chối (ví dụ slot vừa bị người khác đặt).
    Failed = 5
}
