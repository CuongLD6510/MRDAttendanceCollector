using MRDAttendanceCollector.Configuration;

namespace MRDAttendanceCollector.Models;

public sealed class BlackoutService : IBlackoutService
{
    private readonly IReadOnlyList<(TimeSpan Start, TimeSpan End)> _windows;
    private readonly ILogger<BlackoutService> _logger;

    public BlackoutService(IConfiguration configuration, ILogger<BlackoutService> logger)
    {
        _logger = logger;
        var windows = configuration.GetSection("BlackoutWindows").Get<List<BlackoutWindowOptions>>()
            ?? new List<BlackoutWindowOptions>();

        var list = new List<(TimeSpan Start, TimeSpan End)>();
        foreach (var window in windows)
        {
            if (!TimeSpan.TryParse(window.Start, out var start) || !TimeSpan.TryParse(window.End, out var end))
            {
                _logger.LogWarning(
                    "Khoảng Blackout không hợp lệ Start={Start} End={End}; bỏ qua",
                    window.Start,
                    window.End);
                continue;
            }

            list.Add((start, end));
        }

        _windows = list;
    }

    public bool IsInBlackout(DateTime localNow)
    {
        var time = localNow.TimeOfDay;
        foreach (var (start, end) in _windows)
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
}
