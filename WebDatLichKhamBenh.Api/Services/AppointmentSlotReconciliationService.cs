using WebDatLichKhamBenh.Application.Interfaces.Services;
using WebDatLichKhamBenh.Application.Time;

namespace WebDatLichKhamBenh.Api.Services;

public class AppointmentSlotReconciliationService : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<AppointmentSlotReconciliationService> _logger;

    public AppointmentSlotReconciliationService(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<AppointmentSlotReconciliationService> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ReconcileAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = ClinicClock.Now;
            var nextMidnight = now.Date.AddDays(1);
            var delay = nextMidnight - now;

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await ReconcileAsync(stoppingToken);
        }
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var appointmentSlotService = scope.ServiceProvider.GetRequiredService<IAppointmentSlotService>();
            await appointmentSlotService.EnsureRollingWindowAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // App đang dừng, không cần ghi lỗi.
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Không thể reconcile appointment slots. Lần chạy kế tiếp sẽ thử lại.");
        }
    }
}
