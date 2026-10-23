using System.Net;
using System.Net.Sockets;

namespace Aperture.Web.Installation;

/// <summary>
/// Which port Aperture listens on. A first installation takes the first free one from <see cref="ApertureHome.DefaultPort"/>;
/// after that the port stays,
/// because the login, notifications, the app window, shortcuts and calendar feeds all belong to that address.
/// </summary>
public static class Ports
{
    /// <summary>How many ports from the default a first installation tries.</summary>
    public const int Tries = 100;

    /// <summary>
    /// The port to install with: <paramref name="requested"/> (--port), else the installation's current one, else the
    /// first free one from the default. Only a first installation moves on from a port that's taken.
    /// </summary>
    public static int Choose(int? requested, int? current, CommandLog log, Func<int, bool>? isFree = null)
    {
        isFree ??= IsFree;
        if (requested is { } port)
        {
            if (port is < 1 or > 65535)
            {
                throw new InstallationException($"{port} isn't a port: choose one from 1 to 65535.");
            }
            return isFree(port) ? port
                : throw new InstallationException($"Port {port} is in use by another program. Choose another with --port <port>.");
        }

        if (current is { } kept)
        {
            return isFree(kept) ? kept
                : throw new InstallationException($"Port {kept}, Aperture's address, is in use by another program. Close that "
                    + "program and install again, or move Aperture with --port <port>: then its address changes, and with it the "
                    + "login, notifications, shortcuts and calendar feeds.");
        }

        for (var candidate = ApertureHome.DefaultPort; candidate < ApertureHome.DefaultPort + Tries; candidate++)
        {
            if (isFree(candidate))
            {
                if (candidate != ApertureHome.DefaultPort)
                {
                    log.Write($"Port {ApertureHome.DefaultPort} is in use by another program, so Aperture uses {candidate}.");
                }
                return candidate;
            }
        }

        throw new InstallationException($"Ports {ApertureHome.DefaultPort} to {ApertureHome.DefaultPort + Tries - 1} are all "
            + "in use. Choose one with --port <port>.");
    }

    /// <summary>
    /// Whether Aperture could listen on <paramref name="port"/>: it listens on localhost, so on both loopback addresses
    /// (IPv6 only where the computer has it).
    /// </summary>
    public static bool IsFree(int port) =>
        CanListen(IPAddress.Loopback, port) && (!Socket.OSSupportsIPv6 || CanListen(IPAddress.IPv6Loopback, port));

    private static bool CanListen(IPAddress address, int port)
    {
        var listener = new TcpListener(address, port);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Windows otherwise lets a port another program listens on be shared, and its requests be taken.
                listener.ExclusiveAddressUse = true;
            }
            listener.Start();
            return true;
        }
        catch (SocketException e) when (e.SocketErrorCode is SocketError.AddressFamilyNotSupported or SocketError.AddressNotAvailable)
        {
            // No IPv6 loopback here: Kestrel skips it too.
            return address.AddressFamily == AddressFamily.InterNetworkV6;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            listener.Stop();
        }
    }
}
