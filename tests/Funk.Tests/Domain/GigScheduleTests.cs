using Funk.Web.Domain;

namespace Funk.Tests.Domain;

public class GigScheduleTests
{
    private static readonly DateOnly Saturday = new(2026, 12, 12);

    [Fact]
    public void Same_day_gig()
    {
        var (start, end) = GigSchedule.ToRange(Saturday, new TimeOnly(14, 0), new TimeOnly(18, 30));

        Assert.Equal(new DateTime(2026, 12, 12, 14, 0, 0), start);
        Assert.Equal(new DateTime(2026, 12, 12, 18, 30, 0), end);
        Assert.False(GigSchedule.EndsNextDay(new TimeOnly(14, 0), new TimeOnly(18, 30)));
    }

    [Fact]
    public void End_before_start_runs_past_midnight()
    {
        var (start, end) = GigSchedule.ToRange(Saturday, new TimeOnly(22, 0), new TimeOnly(3, 0));

        Assert.Equal(new DateTime(2026, 12, 12, 22, 0, 0), start);
        Assert.Equal(new DateTime(2026, 12, 13, 3, 0, 0), end);
        Assert.True(GigSchedule.EndsNextDay(new TimeOnly(22, 0), new TimeOnly(3, 0)));
    }

    [Fact]
    public void Ending_at_midnight_is_the_next_day()
    {
        var (_, end) = GigSchedule.ToRange(Saturday, new TimeOnly(20, 0), new TimeOnly(0, 0));

        Assert.Equal(new DateTime(2026, 12, 13, 0, 0, 0), end);
    }

    [Fact]
    public void Year_boundary()
    {
        var (_, end) = GigSchedule.ToRange(new DateOnly(2026, 12, 31), new TimeOnly(21, 0), new TimeOnly(4, 0));

        Assert.Equal(new DateTime(2027, 1, 1, 4, 0, 0), end);
    }

    [Theory]
    [InlineData("2026-12-12 22:00", "2026-12-13 03:00", "22:00–03:00")]
    [InlineData("2026-12-12 14:00", "2026-12-12 18:30", "14:00–18:30")]
    [InlineData("2026-12-12 14:00", "2026-12-14 02:00", "14:00–02:00 (+2)")]
    public void FormatTimes(string start, string end, string expected)
    {
        Assert.Equal(expected, GigSchedule.FormatTimes(DateTime.Parse(start), DateTime.Parse(end)));
    }
}
