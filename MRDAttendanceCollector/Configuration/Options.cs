namespace MRDAttendanceCollector.Configuration;

public sealed class SchedulerOptions
{
    public const string SectionName = "Scheduler";

    public string Cron { get; set; } = "0 */1 * * * *";
    public string TimeZone { get; set; } = "SE Asia Standard Time";
    /// <summary>Giữ tương thích config; chu kỳ đồng bộ luôn đọc tuần tự từng máy.</summary>
    public int MaxParallelJobs { get; set; } = 1;
    public int JobTimeoutSeconds { get; set; } = 120;
    public int RetryMaxAttempts { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 10;
    public int DefaultOverlapMinutes { get; set; } = 30;
    /// <summary>
    /// Fallback khi Backend không trả INITIAL_SYNC_FROM.
    /// Máy mới ưu tiên mốc từ kỳ lương hiện tại (đầu kỳ − 1 ngày 23:59).
    /// </summary>
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

    public string BaseUrl { get; set; } = "http://localhost/";
    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 60;
}

public sealed class ReprocessOptions
{
    public const string SectionName = "Reprocess";

    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 10;
    /// <summary>Số job tối đa mỗi lần gọi drain (mỗi job = 1 lần tính bảng công). Clamp 1–30.</summary>
    public int MaxItemsPerDrain { get; set; } = 15;
    /// <summary>Timeout HTTP riêng cho fnDrainAttReprocessQueue (tính bảng công có thể lâu).</summary>
    public int TimeoutSeconds { get; set; } = 300;
}
