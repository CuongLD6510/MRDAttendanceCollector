using MRDAttendanceCollector.Models;

namespace MRDAttendanceCollector.Sdk;

public sealed class MockDeviceSdkAdapter : IDeviceSdkAdapter
{
    private readonly ILogger<MockDeviceSdkAdapter> _logger;

    public MockDeviceSdkAdapter(ILogger<MockDeviceSdkAdapter> logger)
    {
        _logger = logger;
    }

    public string Vendor => "ZKTeco";

    public Task<IReadOnlyList<RawAttendanceLog>> ReadLogsAsync(
        AttendanceDevice device,
        DateTime fromInclusive,
        DateTime toInclusive,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = DateTime.Now;
        var sample = new List<RawAttendanceLog>
        {
            new() { EnrollNumber = "1001", LogTime = now.AddMinutes(-5), AttDeviceId = device.AttDeviceId },
            new() { EnrollNumber = "1002", LogTime = now.AddMinutes(-3), AttDeviceId = device.AttDeviceId }
        };

        _logger.LogInformation(
            "Mock SDK device {DeviceId} returning {Count} sample log(s)",
            device.AttDeviceId,
            sample.Count);

        return Task.FromResult<IReadOnlyList<RawAttendanceLog>>(sample);
    }
}
