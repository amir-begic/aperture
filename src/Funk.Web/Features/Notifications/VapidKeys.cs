using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Funk.Web.Configuration;
using Microsoft.AspNetCore.WebUtilities;

namespace Funk.Web.Features.Notifications;

/// <summary>
/// Funk's own key pair for Web Push (VAPID, RFC 8292). Browsers tie a subscription to the public key, and push
/// services only accept messages signed with the private one. Made once and kept in data/keys (part of the backup);
/// a new pair would mean turning notifications on again on every device.
/// </summary>
public sealed record VapidKeys(
    [property: JsonPropertyName("publicKey")] string PublicKey,
    [property: JsonPropertyName("privateKey")] string PrivateKey)
{
    /// <summary>How long a signature is valid (at most 24 hours are allowed). Short, since each message gets a new one anyway.</summary>
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);

    public static VapidKeys New()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(includePrivateParameters: true);
        return new VapidKeys(
            WebEncoders.Base64UrlEncode(WebPushEncryption.Point(parameters)),
            WebEncoders.Base64UrlEncode(parameters.D!));
    }

    public static VapidKeys LoadOrCreate(FunkPaths paths)
    {
        if (File.Exists(paths.PushKeysFile))
        {
            return JsonSerializer.Deserialize<VapidKeys>(File.ReadAllText(paths.PushKeysFile))
                ?? throw new InvalidDataException($"{paths.PushKeysFile} is empty.");
        }

        var keys = New();
        Directory.CreateDirectory(paths.KeysDirectory);
        var temp = paths.PushKeysFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(keys));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        File.Move(temp, paths.PushKeysFile);
        return keys;
    }

    /// <summary>
    /// The Authorization header for one push service: a JWT signed with ES256 for the service's origin, plus the
    /// public key. <paramref name="subject"/> is a contact for the push service's operators (mailto: or https:).
    /// </summary>
    public string AuthorizationHeader(Uri endpoint, string subject, DateTimeOffset now)
    {
        var header = Encode("""{"typ":"JWT","alg":"ES256"}"""u8);
        var claims = Encode(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["aud"] = endpoint.GetLeftPart(UriPartial.Authority),
            ["exp"] = (now + TokenLifetime).ToUnixTimeSeconds(),
            ["sub"] = subject,
        }));

        using var key = ECDsa.Create(WebPushEncryption.Parameters(
            WebEncoders.Base64UrlDecode(PublicKey), WebEncoders.Base64UrlDecode(PrivateKey)));
        // ES256 wants r and s side by side (IEEE P1363), which is .NET's default.
        var signature = key.SignData(Encoding.ASCII.GetBytes($"{header}.{claims}"), HashAlgorithmName.SHA256);

        return $"vapid t={header}.{claims}.{Encode(signature)}, k={PublicKey}";
    }

    private static string Encode(ReadOnlySpan<byte> bytes) => WebEncoders.Base64UrlEncode(bytes.ToArray());
}
