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
