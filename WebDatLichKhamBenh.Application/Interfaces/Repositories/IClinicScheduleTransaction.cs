namespace WebDatLichKhamBenh.Application.Interfaces.Repositories;

/// <summary>
/// Chạy thao tác đặt hẹn hoặc thay đổi lịch làm nguyên tử, chống tranh chấp giữa các request.
/// Gọi lồng nhau dùng chung transaction của thao tác ngoài.
/// </summary>
public interface IClinicScheduleTransaction
{
    Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default);
    Task ExecuteAsync(Func<Task> action, CancellationToken cancellationToken = default);
}
