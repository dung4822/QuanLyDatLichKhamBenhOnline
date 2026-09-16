using Microsoft.EntityFrameworkCore;
using Npgsql;
using WebDatLichKhamBenh.Application.Exceptions;
using WebDatLichKhamBenh.Application.Interfaces.Repositories;

namespace WebDatLichKhamBenh.Infrastructure.Persistence;

public class ClinicScheduleTransaction : IClinicScheduleTransaction
{
    private readonly AppDbContext _context;

    public ClinicScheduleTransaction(AppDbContext context) => _context = context;

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        if (_context.Database.CurrentTransaction is not null)
            return await action();

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Một khóa chung cho bản đầu: tuần tự hóa các thao tác ghi lịch khám trên mọi API instance.
            // READ COMMITTED đọc dữ liệu mới nhất SAU khi nhận khóa; khóa tự nhả khi commit/rollback.
            await _context.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock(20260912, 1)", cancellationToken);
            var result = await action();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Appointments_AppointmentSlotId" })
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            throw new AppointmentConflictException("Khung giờ này đã có lịch hẹn. Vui lòng chọn giờ khác.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            throw;
        }
    }

    public Task ExecuteAsync(Func<Task> action, CancellationToken cancellationToken = default)
        => ExecuteAsync(async () => { await action(); return true; }, cancellationToken);
}
