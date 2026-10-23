using Aperture.Web.Features.Updates;
using Aperture.Web.Installation;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Aperture.Web.Configuration;

/// <summary>
/// Whether anyone is using Aperture: windows connected to it (Blazor circuits) and requests (the sign-in page has no
/// circuit). Singleton; <see cref="WindowTracker"/> reports the windows, <see cref="IdleStopWorker"/> checks.
/// </summary>
public sealed class AppActivity
{
    /// <summary>Checks in a row without a window or request: a minute after a window closed, five after starting.</summary>
    public const int ChecksAfterWindow = 4;
    public const int ChecksAfterStart = 20;

    private int _windows;
    private int _requests;
    private int _idleChecks;

    public bool HadWindow { get; private set; }

    public bool HasWindow => Volatile.Read(ref _windows) > 0;

    /// <summary>
    /// One check: true once Aperture has gone unused long enough to stop. <paramref name="busy"/> is other work that
    /// mustn't be cut off, such as an update downloading.
    /// </summary>
    public bool CheckUnused(bool busy = false)
    {
        var used = busy || HasWindow || Interlocked.Exchange(ref _requests, 0) > 0;
        _idleChecks = used ? 0 : _idleChecks + 1;
        return _idleChecks >= (HadWindow ? ChecksAfterWindow : ChecksAfterStart);
    }

    public void WindowConnected()
    {
        Interlocked.Increment(ref _windows);
        HadWindow = true;
    }

    public void WindowDisconnected() => Interlocked.Decrement(ref _windows);

    public void Request() => Interlocked.Increment(ref _requests);
}

/// <summary>Counts a window as open while its connection is up; a closed window drops it at once.</summary>
public sealed class WindowTracker(AppActivity activity) : CircuitHandler
{
    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        activity.WindowConnected();
        return Task.CompletedTask;
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        activity.WindowDisconnected();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Stops Aperture when nobody uses it, so it takes no memory while its window is closed: a minute after the last window
/// closed, or five minutes after starting at logon if no window was opened (time for the notifications that are due).
/// The launcher starts it again (see Installer.OpenAsync). Counted in checks rather than by the clock, so a computer
/// waking from sleep gets its window reconnected before Aperture decides it's unused. Not while an update downloads.
/// </summary>
public sealed class IdleStopWorker(
    AppActivity activity, UpdateService updates, Platform platform, ApertureHome home, IHostApplicationLifetime lifetime,
    ILogger<IdleStopWorker> logger) : BackgroundService
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(CheckInterval, stoppingToken);
                if (activity.CheckUnused(busy: updates.IsDownloading || updates.IsRequestPending))
                {
                    logger.LogInformation("No Aperture window open: stopping until it's opened again");
                    await platform.StopWhenIdleAsync(home);
                    lifetime.StopApplication();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
