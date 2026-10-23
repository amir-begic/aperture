using Aperture.Web.Configuration;

namespace Aperture.Tests.Configuration;

public sealed class AppActivityTests
{
    private static int ChecksUntilUnused(AppActivity activity, int limit = 100)
    {
        for (var checks = 1; checks <= limit; checks++)
        {
            if (activity.CheckUnused())
            {
                return checks;
            }
        }
        return -1;
    }

    [Fact]
    public void Started_at_logon_it_waits_five_minutes_for_a_window()
    {
        Assert.Equal(AppActivity.ChecksAfterStart, ChecksUntilUnused(new AppActivity()));
    }

    [Fact]
    public void An_open_window_keeps_it_running()
    {
        var activity = new AppActivity();
        activity.WindowConnected();

        Assert.Equal(-1, ChecksUntilUnused(activity));
    }

    [Fact]
    public void A_minute_after_the_last_window_closed_it_stops()
    {
        var activity = new AppActivity();
        activity.WindowConnected();
        activity.WindowConnected();
        activity.WindowDisconnected();
        Assert.False(activity.CheckUnused()); // one still open

        activity.WindowDisconnected();

        Assert.Equal(AppActivity.ChecksAfterWindow, ChecksUntilUnused(activity));
    }

    [Fact]
    public void A_reload_or_a_request_starts_the_minute_over()
    {
        var activity = new AppActivity();
        activity.WindowConnected();
        activity.WindowDisconnected();
        for (var i = 1; i < AppActivity.ChecksAfterWindow; i++) Assert.False(activity.CheckUnused());

        activity.Request(); // e.g. the sign-in page, which has no window connection

        Assert.False(activity.CheckUnused());
        Assert.Equal(AppActivity.ChecksAfterWindow, ChecksUntilUnused(activity));
    }

    [Fact]
    public void Busy_work_such_as_an_update_download_keeps_it_running()
    {
        var activity = new AppActivity();
        activity.WindowConnected();
        activity.WindowDisconnected();

        for (var i = 0; i < 10; i++) Assert.False(activity.CheckUnused(busy: true));
    }
}
