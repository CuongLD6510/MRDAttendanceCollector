namespace MRDAttendanceCollector.Models;

public interface IAttendanceBackendClient
{
    Task<IReadOnlyList<AttendanceDevice>> GetActiveDevicesAsync(CancellationToken cancellationToken);

    Task<PostRawLogsResult> PostRawLogsAsync(
        int attDeviceId,
        IReadOnlyList<RawAttendanceLog> logs,
        CancellationToken cancellationToken);

    Task PostSyncResultAsync(SyncJobResult result, CancellationToken cancellationToken);

    Task<DrainReprocessResult> DrainReprocessQueueAsync(int maxItems, CancellationToken cancellationToken);
}

public interface IDeviceSdkAdapter
{
    string Vendor { get; }

    Task<IReadOnlyList<RawAttendanceLog>> ReadLogsAsync(
        AttendanceDevice device,
        DateTime fromInclusive,
        DateTime toInclusive,
        CancellationToken cancellationToken);
}

public interface IBlackoutService
{
    bool IsInBlackout(DateTime localNow);
}
