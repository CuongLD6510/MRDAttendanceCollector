namespace MRDAttendanceCollector.Models;

public sealed class RawAttendanceLog
{
    public string EnrollNumber { get; set; } = string.Empty;
    public DateTime LogTime { get; set; }
    public DateTime WorkDate { get; set; }
    public int AttDeviceId { get; set; }
}
