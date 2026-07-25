using MRDAttendanceCollector.Configuration;

namespace MRDAttendanceCollector.Models;

public sealed class BlackoutService : IBlackoutService
{
    private readonly object _gate = new();
    private IReadOnlyList<(TimeSpan Start, TimeSpan End)> _windows = Array.Empty<(TimeSpan, TimeSpan)>();
    private readonly ILogger<BlackoutService> _logger;

    public BlackoutService(ILogger<BlackoutService> logger)
    {
        _logger = logger;
    }

    public void SetWindows(IReadOnlyList<BlackoutWindowOptions> windows)
    {
        var parsed = ParseWindows(windows ?? Array.Empty<BlackoutWindowOptions>());
        lock (_gate)
        {
            _windows = parsed;
        }
    }

    public bool IsInBlackout(DateTime localNow)
    {
        IReadOnlyList<(TimeSpan Start, TimeSpan End)> windows;
        lock (_gate)
        {
            windows = _windows;
        }

        var time = localNow.TimeOfDay;
        foreach (var (start, end) in windows)
        {
            if (start <= end)
            {
                if (time >= start && time < end)
                {
                    return true;
                }
            }
            else if (time >= start || time < end)
            {
                return true;
            }
        }

        return false;
    }

    private List<(TimeSpan Start, TimeSpan End)> ParseWindows(IEnumerable<BlackoutWindowOptions> windows)
    {
        var list = new List<(TimeSpan Start, TimeSpan End)>();
        foreach (var window in windows)
        {
            if (!TryParseBlackoutTime(window.Start, allowEndOfDay: false, out var start)
                || !TryParseBlackoutTime(window.End, allowEndOfDay: true, out var end))
            {
                _logger.LogWarning(
                    "Khoảng tạm dừng đồng bộ không hợp lệ Start={Start} End={End}; bỏ qua",
                    window.Start,
                    window.End);
                continue;
            }

            if (start == end)
            {
                _logger.LogWarning(
                    "Khoảng tạm dừng đồng bộ Start=End={Start}; bỏ qua",
                    window.Start);
                continue;
            }

            list.Add((start, end));
        }

        return list;
    }

    private static bool TryParseBlackoutTime(string? text, bool allowEndOfDay, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var t = text.Trim();
        if (allowEndOfDay && (t == "24:00" || t == "24:00:00"))
        {
            result = TimeSpan.FromDays(1);
            return true;
        }

        return TimeSpan.TryParse(t, out result);
    }
}
