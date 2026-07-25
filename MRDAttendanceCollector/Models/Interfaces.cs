using MRDAttendanceCollector.Configuration;

namespace MRDAttendanceCollector.Models;

public interface IAttendanceBackendClient
{
    Task<CollectorDevicesSnapshot> GetActiveDevicesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<BlackoutWindowOptions>> GetBlackoutWindowsAsync(CancellationToken cancellationToken);

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

    void SetWindows(IReadOnlyList<BlackoutWindowOptions> windows);
}
