namespace MRDAttendanceCollector.Configuration;

public sealed class SchedulerOptions
{
    public const string SectionName = "Scheduler";

    public string Cron { get; set; } = "0 */1 * * * *";
    public string TimeZone { get; set; } = "SE Asia Standard Time";
    public int MaxParallelJobs { get; set; } = 10;
    public int JobTimeoutSeconds { get; set; } = 120;
    public int RetryMaxAttempts { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 10;
    public int DefaultOverlapMinutes { get; set; } = 30;
    public DateTime InitialSyncFromDate { get; set; } = new(2026, 1, 1);
}

public sealed class BlackoutWindowOptions
{
    public string Start { get; set; } = "00:00";
    public string End { get; set; } = "00:00";
}

public sealed class BackendOptions
{
    public const string SectionName = "Backend";

    public bool UseMock { get; set; } = true;
    public string BaseUrl { get; set; } = "http://localhost/";
    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 60;
}

public sealed class MockDeviceOptions
{
    public int AttDeviceId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = "127.0.0.1";
    public int PortNo { get; set; } = 4370;
    public int MachineNumber { get; set; } = 1;
    public DateTime? LastProcessedLogTime { get; set; }
}
