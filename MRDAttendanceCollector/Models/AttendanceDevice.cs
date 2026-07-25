namespace MRDAttendanceCollector.Models;

public sealed class AttendanceDevice
{
    public int AttDeviceId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int PortNo { get; set; } = 4370;
    public int MachineNumber { get; set; } = 1;
    public DateTime? LastProcessedLogTime { get; set; }
    public string DeviceVendor { get; set; } = "ZKTeco";
}

/// <summary>
/// Kết quả fnGetCollectorDevices: danh sách máy + mốc đọc lần đầu (đầu kỳ lương − 1 ngày 23:59).
/// </summary>
public sealed class CollectorDevicesSnapshot
{
    public IReadOnlyList<AttendanceDevice> Devices { get; init; } = Array.Empty<AttendanceDevice>();
    /// <summary>Null nếu Backend không trả — Collector fallback Scheduler:InitialSyncFromDate.</summary>
    public DateTime? InitialSyncFrom { get; init; }
}
