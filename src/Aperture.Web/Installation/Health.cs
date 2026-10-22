namespace Aperture.Web.Installation;

/// <summary>Whether a started Aperture is up and can reach its database (/health).</summary>
public static class Health
{
    public static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(1);

    public static async Task<bool> WaitAsync(int port, TimeSpan? timeout = null)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var until = DateTime.UtcNow + (timeout ?? StartTimeout);
        while (DateTime.UtcNow < until)
        {
            try
            {
                if (await http.GetStringAsync($"http://127.0.0.1:{port}/health") == "ok")
                {
                    return true;
                }
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                // Not up yet.
            }
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        return false;
    }
}

/// <summary>
/// What a command reports. launchd and systemd write the console to the log files; on Windows there's no console
/// (Aperture runs without a window), so it's written to logs/&lt;command&gt;.log directly.
/// </summary>
public sealed class CommandLog(string? file)
{
    public static CommandLog For(ApertureHome home, string command) =>
        new(OperatingSystem.IsWindows() ? home.Log(command) : null);

    public void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";
        Console.WriteLine(line);
        if (file is not null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.AppendAllText(file, line + Environment.NewLine);
            }
            catch (IOException)
            {
                // Logging must never stop an update or a backup.
            }
        }
    }

    /// <summary>The last lines of Aperture's log, after it didn't start.</summary>
    public void Tail(ApertureHome home, int lines = 30)
    {
        try
        {
            using var stream = new FileStream(home.Log("aperture"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            Write("Last lines of the log:" + Environment.NewLine
                + string.Join(Environment.NewLine, reader.ReadToEnd().Split('\n').TakeLast(lines)));
        }
        catch (IOException)
        {
            Write($"No log at {home.Log("aperture")}.");
        }
    }
}
