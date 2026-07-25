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
        _logger.LogInformation("Bắt đầu chu kỳ đồng bộ");

        CollectorDevicesSnapshot snapshot;
        try
        {
            snapshot = await _backend.GetActiveDevicesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không lấy được danh sách máy từ Backend");
            return;
        }

        var devices = snapshot.Devices;
        if (devices.Count == 0)
        {
            _logger.LogInformation("Không có máy ACTIVE để đồng bộ");
            return;
        }

        var initialSyncFrom = snapshot.InitialSyncFrom ?? _scheduler.InitialSyncFromDate;
        _logger.LogInformation(
            "Đồng bộ tuần tự {Count} máy (mỗi máy: đọc SDK → ghi raw → cập nhật sync). Mốc máy mới={InitialSyncFrom:o}",
            devices.Count,
            initialSyncFrom);

        foreach (var device in devices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await SyncDeviceSequentialAsync(device, initialSyncFrom, cancellationToken);
        }

        _logger.LogInformation("Kết thúc chu kỳ đồng bộ");
    }

    private async Task SyncDeviceSequentialAsync(
        AttendanceDevice device,
        DateTime initialSyncFrom,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_scheduler.JobTimeoutSeconds));
            await SyncOneDeviceAsync(device, initialSyncFrom, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                "Máy {DeviceId} ({Name}) hết thời gian chờ sau {Seconds} giây",
                device.AttDeviceId,
                device.DeviceName,
                _scheduler.JobTimeoutSeconds);

            await TryPostSyncResultAsync(new SyncJobResult
            {
                AttDeviceId = device.AttDeviceId,
                JobStartTime = DateTime.Now,
                JobEndTime = DateTime.Now,
                JobStatus = SyncJobStatuses.Timeout,
                ErrorMessage = $"Hết thời gian chờ sau {_scheduler.JobTimeoutSeconds} giây",
                LastProcessedLogTime = device.LastProcessedLogTime
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi không mong đợi khi đồng bộ máy {DeviceId}", device.AttDeviceId);
        }
    }

    private async Task SyncOneDeviceAsync(
        AttendanceDevice device,
        DateTime initialSyncFrom,
        CancellationToken cancellationToken)
    {
        var jobStart = DateTime.Now;
        var retryCount = 0;
        Exception? lastError = null;

        // Máy mới: neo tại (đầu kỳ lương − 1) 23:59 từ Backend; máy đã sync: LastProcessedLogTime.
        var anchor = device.LastProcessedLogTime ?? initialSyncFrom;
        var from = anchor.AddMinutes(-_scheduler.DefaultOverlapMinutes);
        var to = DateTime.Now;

        _logger.LogInformation(
            "Máy {DeviceId} ({Name}) khoảng đọc {From:o} → {To:o}",
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
                var normalized = NormalizeLogs(device.AttDeviceId, rawLogs, out var skippedEmptyEnroll);
                if (skippedEmptyEnroll > 0)
                {
                    _logger.LogWarning(
                        "Máy {DeviceId} bỏ qua {Count} bản ghi thiếu mã chấm công",
                        device.AttDeviceId,
                        skippedEmptyEnroll);
                }

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
                    "Máy {DeviceId} THÀNH CÔNG Đọc={Read} Thêm mới={Inserted} Trùng={Duplicate}",
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
                    "Máy {DeviceId} lần thử {Attempt}/{Max} thất bại",
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
            ErrorMessage = lastError?.Message ?? "Lỗi không xác định"
        }, cancellationToken);

        _logger.LogError(
            lastError,
            "Máy {DeviceId} THẤT BẠI sau {Attempts} lần thử",
            device.AttDeviceId,
            retryCount);
    }

    private static List<RawAttendanceLog> NormalizeLogs(
        int attDeviceId,
        IReadOnlyList<RawAttendanceLog> rawLogs,
        out int skippedEmptyEnroll)
    {
        skippedEmptyEnroll = 0;
        var result = new List<RawAttendanceLog>(rawLogs.Count);
        foreach (var log in rawLogs)
        {
            if (string.IsNullOrWhiteSpace(log.EnrollNumber))
            {
                skippedEmptyEnroll++;
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
            _logger.LogError(ex, "Không gửi được kết quả đồng bộ của máy {DeviceId}", result.AttDeviceId);
        }
    }
}
