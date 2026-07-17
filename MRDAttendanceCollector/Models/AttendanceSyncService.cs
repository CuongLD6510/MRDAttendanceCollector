using Microsoft.Extensions.Options;
using MRDAttendanceCollector.Configuration;

namespace MRDAttendanceCollector.Models;

public sealed class AttendanceSyncService
{
    private readonly IAttendanceBackendClient _backend;
    private readonly IDeviceSdkAdapter _adapter;
    private readonly SchedulerOptions _scheduler;
    private readonly ILogger<AttendanceSyncService> _logger;

    public AttendanceSyncService(
        IAttendanceBackendClient backend,
        IDeviceSdkAdapter adapter,
        IOptions<SchedulerOptions> schedulerOptions,
        ILogger<AttendanceSyncService> logger)
    {
        _backend = backend;
        _adapter = adapter;
        _scheduler = schedulerOptions.Value;
        _logger = logger;
    }

    public async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Sync cycle started");

        IReadOnlyList<AttendanceDevice> devices;
        try
        {
            devices = await _backend.GetActiveDevicesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get active devices from backend");
            return;
        }

        if (devices.Count == 0)
        {
            _logger.LogInformation("No active devices to sync");
            return;
        }

        _logger.LogInformation("Syncing {Count} device(s), MaxParallel={MaxParallel}", devices.Count, _scheduler.MaxParallelJobs);

        using var semaphore = new SemaphoreSlim(_scheduler.MaxParallelJobs, _scheduler.MaxParallelJobs);
        var tasks = devices.Select(device => SyncDeviceWithThrottleAsync(device, semaphore, cancellationToken));
        await Task.WhenAll(tasks);

        _logger.LogInformation("Sync cycle finished");
    }

    private async Task SyncDeviceWithThrottleAsync(
        AttendanceDevice device,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_scheduler.JobTimeoutSeconds));
            await SyncOneDeviceAsync(device, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                "Device {DeviceId} ({Name}) job timed out after {Seconds}s",
                device.AttDeviceId,
                device.DeviceName,
                _scheduler.JobTimeoutSeconds);

            await TryPostSyncResultAsync(new SyncJobResult
            {
                AttDeviceId = device.AttDeviceId,
                JobStartTime = DateTime.Now,
                JobEndTime = DateTime.Now,
                JobStatus = SyncJobStatuses.Timeout,
                ErrorMessage = $"Job timed out after {_scheduler.JobTimeoutSeconds}s",
                LastProcessedLogTime = device.LastProcessedLogTime
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error syncing device {DeviceId}", device.AttDeviceId);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task SyncOneDeviceAsync(AttendanceDevice device, CancellationToken cancellationToken)
    {
        var jobStart = DateTime.Now;
        var retryCount = 0;
        Exception? lastError = null;

        var anchor = device.LastProcessedLogTime ?? _scheduler.InitialSyncFromDate;
        var from = anchor.AddMinutes(-_scheduler.DefaultOverlapMinutes);
        var to = DateTime.Now;

        _logger.LogInformation(
            "Device {DeviceId} ({Name}) read range {From:o} → {To:o}",
            device.AttDeviceId,
            device.DeviceName,
            from,
            to);

        while (retryCount < _scheduler.RetryMaxAttempts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var rawLogs = await _adapter.ReadLogsAsync(device, from, to, cancellationToken);
                var normalized = NormalizeLogs(device.AttDeviceId, rawLogs);
                var postResult = await _backend.PostRawLogsAsync(device.AttDeviceId, normalized, cancellationToken);

                var maxLogTime = normalized.Count > 0
                    ? normalized.Max(x => x.LogTime)
                    : device.LastProcessedLogTime;

                var result = new SyncJobResult
                {
                    AttDeviceId = device.AttDeviceId,
                    JobStartTime = jobStart,
                    JobEndTime = DateTime.Now,
                    ReadFromTime = from,
                    LastProcessedLogTime = maxLogTime,
                    RecordsRead = normalized.Count,
                    RecordsInserted = postResult.Inserted,
                    RecordsDuplicate = postResult.Duplicate,
                    RetryCount = retryCount,
                    JobStatus = SyncJobStatuses.Success
                };

                await _backend.PostSyncResultAsync(result, cancellationToken);

                _logger.LogInformation(
                    "Device {DeviceId} SUCCESS Read={Read} Inserted={Inserted} Duplicate={Duplicate}",
                    device.AttDeviceId,
                    result.RecordsRead,
                    result.RecordsInserted,
                    result.RecordsDuplicate);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                retryCount++;
                _logger.LogWarning(
                    ex,
                    "Device {DeviceId} attempt {Attempt}/{Max} failed",
                    device.AttDeviceId,
                    retryCount,
                    _scheduler.RetryMaxAttempts);

                if (retryCount < _scheduler.RetryMaxAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_scheduler.RetryDelaySeconds), cancellationToken);
                }
            }
        }

        await TryPostSyncResultAsync(new SyncJobResult
        {
            AttDeviceId = device.AttDeviceId,
            JobStartTime = jobStart,
            JobEndTime = DateTime.Now,
            ReadFromTime = from,
            LastProcessedLogTime = device.LastProcessedLogTime,
            RetryCount = retryCount,
            JobStatus = SyncJobStatuses.Failed,
            ErrorMessage = lastError?.Message ?? "Unknown error"
        }, cancellationToken);

        _logger.LogError(
            lastError,
            "Device {DeviceId} FAILED after {Attempts} attempts",
            device.AttDeviceId,
            retryCount);
    }

    private static List<RawAttendanceLog> NormalizeLogs(int attDeviceId, IReadOnlyList<RawAttendanceLog> rawLogs)
    {
        var result = new List<RawAttendanceLog>(rawLogs.Count);
        foreach (var log in rawLogs)
        {
            if (string.IsNullOrWhiteSpace(log.EnrollNumber))
            {
                continue;
            }

            result.Add(new RawAttendanceLog
            {
                EnrollNumber = log.EnrollNumber.Trim(),
                LogTime = log.LogTime,
                WorkDate = WorkDateHelper.CalculateWorkDate(log.LogTime),
                AttDeviceId = attDeviceId
            });
        }

        return result;
    }

    private async Task TryPostSyncResultAsync(SyncJobResult result, CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            await _backend.PostSyncResultAsync(result, cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post sync result for device {DeviceId}", result.AttDeviceId);
        }
    }
}
