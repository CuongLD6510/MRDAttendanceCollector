using Cronos;
using Microsoft.Extensions.Options;
using MRDAttendanceCollector.Configuration;
using MRDAttendanceCollector.Models;

namespace MRDAttendanceCollector.Scheduling;

public sealed class CronSyncHostedService : BackgroundService
{
    private readonly AttendanceSyncService _syncService;
    private readonly IBlackoutService _blackoutService;
    private readonly SchedulerOptions _options;
    private readonly ILogger<CronSyncHostedService> _logger;
    private readonly CronExpression _cron;
    private readonly TimeZoneInfo _timeZone;

    public CronSyncHostedService(
        AttendanceSyncService syncService,
        IBlackoutService blackoutService,
        IOptions<SchedulerOptions> options,
        ILogger<CronSyncHostedService> logger)
    {
        _syncService = syncService;
        _blackoutService = blackoutService;
        _options = options.Value;
        _logger = logger;
        _cron = CronExpression.Parse(_options.Cron, CronFormat.IncludeSeconds);
        _timeZone = ResolveTimeZone(_options.TimeZone);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Cron sync hosted service started. Cron={Cron} TimeZone={TimeZone}",
            _options.Cron,
            _timeZone.Id);

        while (!stoppingToken.IsCancellationRequested)
        {
            var utcNow = DateTimeOffset.UtcNow;
            var next = _cron.GetNextOccurrence(utcNow, _timeZone);
            if (next is null)
            {
                _logger.LogError("Cron expression produced no next occurrence; stopping scheduler loop");
                break;
            }

            var delay = next.Value - utcNow;
            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _timeZone).DateTime;
            if (_blackoutService.IsInBlackout(localNow))
            {
                _logger.LogWarning("Sync skipped due to blackout window. LocalTime={LocalTime:HH:mm:ss}", localNow);
                continue;
            }

            try
            {
                await _syncService.RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error during sync cycle");
            }
        }

        _logger.LogInformation("Cron sync hosted service stopped");
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Local;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }
}
