namespace MRDAttendanceCollector.Models;

public sealed class SyncJobResult
{
    public int AttDeviceId { get; set; }
    public DateTime JobStartTime { get; set; }
    public DateTime JobEndTime { get; set; }
    public DateTime? ReadFromTime { get; set; }
    public DateTime? LastProcessedLogTime { get; set; }
    public int RecordsRead { get; set; }
    public int RecordsInserted { get; set; }
    public int RecordsDuplicate { get; set; }
    public int RetryCount { get; set; }
    public string JobStatus { get; set; } = SyncJobStatuses.Success;
    public string? ErrorMessage { get; set; }
}

public static class SyncJobStatuses
{
    public const string Success = "SUCCESS";
    public const string Failed = "FAILED";
    public const string Timeout = "TIMEOUT";
}

public sealed class PostRawLogsResult
{
    public int Inserted { get; set; }
    public int Duplicate { get; set; }
}

public sealed class DrainReprocessResult
{
    public int Processed { get; set; }
    public int Failed { get; set; }
    public int Remaining { get; set; }
}
