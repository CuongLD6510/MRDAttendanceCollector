namespace MRDAttendanceCollector.Models;

public static class WorkDateHelper
{
    public static readonly TimeSpan WorkDayStart = new(6, 0, 0);

    public static DateTime CalculateWorkDate(DateTime logTime)
    {
        var date = logTime.Date;
        if (logTime.TimeOfDay < WorkDayStart)
        {
            return date.AddDays(-1);
        }

        return date;
    }
}
