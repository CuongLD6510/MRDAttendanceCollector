using MRDAttendanceCollector.Configuration;
using MRDAttendanceCollector.Models;

namespace MRDAttendanceCollector.Backend;

public sealed class MockAttendanceBackendClient : IAttendanceBackendClient
{
    private readonly IReadOnlyList<MockDeviceOptions> _mockDevices;
    private readonly ILogger<MockAttendanceBackendClient> _logger;

    public MockAttendanceBackendClient(IConfiguration configuration, ILogger<MockAttendanceBackendClient> logger)
    {
        _mockDevices = configuration.GetSection("MockDevices").Get<List<MockDeviceOptions>>()
            ?? new List<MockDeviceOptions>();
        _logger = logger;
    }

    public Task<IReadOnlyList<AttendanceDevice>> GetActiveDevicesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var devices = _mockDevices.Select(d => new AttendanceDevice
        {
            AttDeviceId = d.AttDeviceId,
            DeviceName = d.DeviceName,
            IpAddress = d.IpAddress,
            PortNo = d.PortNo,
            MachineNumber = d.MachineNumber,
            LastProcessedLogTime = d.LastProcessedLogTime,
            DeviceVendor = "ZKTeco"
        }).ToList();

        if (devices.Count == 0)
        {
            devices.Add(new AttendanceDevice
            {
                AttDeviceId = 1,
                DeviceName = "Mock Device 1",
                IpAddress = "127.0.0.1",
                PortNo = 4370,
                MachineNumber = 1,
                DeviceVendor = "ZKTeco"
            });
        }

        _logger.LogInformation("Mock Backend trả về {Count} máy", devices.Count);
        return Task.FromResult<IReadOnlyList<AttendanceDevice>>(devices);
    }

    public Task<PostRawLogsResult> PostRawLogsAsync(
        int attDeviceId,
        IReadOnlyList<RawAttendanceLog> logs,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation(
            "Mock PostRawLogs máy={DeviceId} số bản ghi={Count}",
            attDeviceId,
            logs.Count);
        return Task.FromResult(new PostRawLogsResult { Inserted = logs.Count, Duplicate = 0 });
    }

    public Task PostSyncResultAsync(SyncJobResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation(
            "Mock PostSyncResult máy={DeviceId} trạng thái={Status} đọc={Read} thêm={Inserted} trùng={Duplicate}",
            result.AttDeviceId,
            result.JobStatus,
            result.RecordsRead,
            result.RecordsInserted,
            result.RecordsDuplicate);
        return Task.CompletedTask;
    }
}
