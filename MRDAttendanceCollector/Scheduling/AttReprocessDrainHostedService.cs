using Microsoft.Extensions.Options;
using MRDAttendanceCollector.Configuration;
using MRDAttendanceCollector.Models;

namespace MRDAttendanceCollector.Scheduling;

public sealed class AttReprocessDrainHostedService : BackgroundService
{
    private readonly IAttendanceBackendClient _backend;
    private readonly ReprocessOptions _options;
    private readonly ILogger<AttReprocessDrainHostedService> _logger;

    public AttReprocessDrainHostedService(
        IAttendanceBackendClient backend,
        IOptions<ReprocessOptions> options,
        ILogger<AttReprocessDrainHostedService> logger)
    {
        _backend = backend;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("AttReprocessDrainHostedService đã tắt (Reprocess:Enabled=false)");
            return;
        }

        var intervalSeconds = Math.Max(10, _options.IntervalSeconds);
        var maxItems = Math.Max(1, Math.Min(30, _options.MaxItemsPerDrain));
        var timeoutSeconds = Math.Max(60, _options.TimeoutSeconds);

        _logger.LogInformation(
            "AttReprocessDrainHostedService đã khởi động. Interval={Interval}s MaxItems={MaxItems} Timeout={Timeout}s",
            intervalSeconds,
            maxItems,
            timeoutSeconds);

        // Drain ngay lần đầu, sau đó mới chờ Interval — giảm độ trễ sau phân ca / đổi ca.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await _backend.DrainReprocessQueueAsync(maxItems, stoppingToken);
                if (result.Processed > 0 || result.Failed > 0 || result.Remaining > 0)
                {
                    _logger.LogInformation(
                        "Drain queue: Processed={Processed} Failed={Failed} Remaining={Remaining}",
                        result.Processed,
                        result.Failed,
                        result.Remaining);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Drain hàng đợi bảng công hết thời gian chờ ({Timeout}s). Job kẹt PROCESSING sẽ được backend trả lại PENDING.",
                    timeoutSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi drain hàng đợi tính lại bảng công");
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("AttReprocessDrainHostedService đã dừng");
    }
}
