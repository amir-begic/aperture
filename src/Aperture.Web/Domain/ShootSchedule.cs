namespace Aperture.Web.Domain;

public static class ShootSchedule
{
    /// <summary>
    /// Builds a shoot's start and end from a date and two times. Shoots often run past midnight,
    /// so an end time at or before the start time means the next day (22:00–03:00).
    /// </summary>
    public static (DateTime Start, DateTime End) ToRange(DateOnly date, TimeOnly startTime, TimeOnly endTime)
    {
        var start = date.ToDateTime(startTime);
        var end = date.ToDateTime(endTime);
        return (start, end <= start ? end.AddDays(1) : end);
    }

    public static bool EndsNextDay(TimeOnly startTime, TimeOnly endTime) => endTime <= startTime;

    /// <summary>"22:00–03:00", or with "(+1)" when that's not obvious from the times.</summary>
    public static string FormatTimes(DateTime start, DateTime end) =>
        $"{start:HH:mm}–{end:HH:mm}" + ((end.Date - start.Date).Days > 1 ? $" (+{(end.Date - start.Date).Days})" : "");
}
