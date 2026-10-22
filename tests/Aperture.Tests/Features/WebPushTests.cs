using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aperture.Web.Configuration;
using Aperture.Web.Features.Notifications;
using Microsoft.AspNetCore.WebUtilities;

namespace Aperture.Tests.Features;

public class WebPushTests : IDisposable
{
    private readonly string _dataDirectory = Directory.CreateTempSubdirectory("aperture-push-").FullName;

    public void Dispose() => Directory.Delete(_dataDirectory, recursive: true);

    private static byte[] B64(string text) => WebEncoders.Base64UrlDecode(text);

    [Fact]
    public void Encryption_matches_the_example_in_RFC_8291()
    {
        // RFC 8291, section 5 and appendix A.
        var serverPublic = B64("BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8");
        using var serverKey = ECDiffieHellman.Create(
            WebPushEncryption.Parameters(serverPublic, B64("yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw")));

        var message = WebPushEncryption.Encrypt(
            "When I grow up, I want to be a watermelon"u8,
            B64("BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4"),
            B64("BTBZMqHH6r4Tts7J_aSIgg"),
            serverKey,
            B64("DGv6ra1nlYgDCS1FRnbzlw"));

        // The message body in section 5.
        Assert.Equal(
            "DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27ml"
            + "mlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A_yl95bQpu6cVPT"
            + "pK4Mqgkf1CXztLVBSt2Ks3oZwbuwXPXLWyouBWLVWGNWQexSgSxsj_Qulcy4a-fN",
            WebEncoders.Base64UrlEncode(message));
    }

    [Fact]
    public void The_device_can_decrypt_a_message()
    {
        var device = TestDevice.Create();

        var message = WebPushEncryption.Encrypt("""{"title":"Grüezi"}"""u8, device.PublicKey, device.AuthSecret);

        Assert.Equal("""{"title":"Grüezi"}""", device.Decrypt(message));
    }

    [Fact]
    public void Messages_that_are_too_large_are_refused()
    {
        var device = TestDevice.Create();

        Assert.Throws<ArgumentException>(() =>
            WebPushEncryption.Encrypt(new byte[WebPushEncryption.MaxPayload + 1], device.PublicKey, device.AuthSecret));
        Assert.Equal(4096, WebPushEncryption.Encrypt(new byte[WebPushEncryption.MaxPayload], device.PublicKey, device.AuthSecret).Length);
    }

    [Fact]
    public void Keys_are_made_once_and_kept()
    {
        var paths = new AperturePaths(_dataDirectory);

        var first = VapidKeys.LoadOrCreate(paths);
        var second = VapidKeys.LoadOrCreate(paths);

        Assert.Equal(first, second);
        Assert.Equal(65, B64(first.PublicKey).Length);
        Assert.Equal(32, B64(first.PrivateKey).Length);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(paths.PushKeysFile));
        }
    }

    [Fact]
    public void Authorization_is_a_signed_token_for_the_push_service()
    {
        var keys = VapidKeys.New();
        var now = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

        var header = keys.AuthorizationHeader(new Uri("https://web.push.apple.com/QGuQyavXutnMH"), "mailto:studio@example.ch", now);

        var parts = header.Split(", ");
        Assert.StartsWith("vapid t=", parts[0]);
        Assert.Equal($"k={keys.PublicKey}", parts[1]);

        var jwt = parts[0]["vapid t=".Length..].Split('.');
        Assert.Equal("""{"typ":"JWT","alg":"ES256"}""", Encoding.UTF8.GetString(B64(jwt[0])));
        using var claims = JsonDocument.Parse(B64(jwt[1]));
        Assert.Equal("https://web.push.apple.com", claims.RootElement.GetProperty("aud").GetString());
        Assert.Equal((now + VapidKeys.TokenLifetime).ToUnixTimeSeconds(), claims.RootElement.GetProperty("exp").GetInt64());
        Assert.Equal("mailto:studio@example.ch", claims.RootElement.GetProperty("sub").GetString());

        using var verifier = ECDsa.Create(WebPushEncryption.Parameters(B64(keys.PublicKey)));
        Assert.True(verifier.VerifyData(Encoding.ASCII.GetBytes($"{jwt[0]}.{jwt[1]}"), B64(jwt[2]), HashAlgorithmName.SHA256));
    }

    [Fact]
    public void Only_https_endpoints_with_proper_keys_are_accepted()
    {
        var device = TestDevice.Create();
        var valid = device.Subscription("https://web.push.apple.com/abc");

        Assert.True(PushService.IsValid(valid));
        Assert.False(PushService.IsValid(valid with { Endpoint = "http://web.push.apple.com/abc" }));
        Assert.False(PushService.IsValid(valid with { Endpoint = "not a url" }));
        Assert.False(PushService.IsValid(valid with { Auth = WebEncoders.Base64UrlEncode(new byte[8]) }));
        Assert.False(PushService.IsValid(valid with { P256dh = WebEncoders.Base64UrlEncode(new byte[65]) }));
        Assert.False(PushService.IsValid(valid with { P256dh = "!!" }));
    }
}

/// <summary>A browser's side of a push subscription: its key pair and auth secret, and decrypting what arrives.</summary>
public sealed class TestDevice
{
    private readonly ECParameters _key;

    private TestDevice(ECParameters key, byte[] authSecret)
    {
        _key = key;
        AuthSecret = authSecret;
    }

    public byte[] PublicKey => WebPushEncryption.Point(_key);

    public byte[] AuthSecret { get; }

    public static TestDevice Create()
    {
        using var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return new TestDevice(key.ExportParameters(includePrivateParameters: true), RandomNumberGenerator.GetBytes(16));
    }

    public BrowserSubscription Subscription(string endpoint) =>
        new(endpoint, WebEncoders.Base64UrlEncode(PublicKey), WebEncoders.Base64UrlEncode(AuthSecret));

    /// <summary>RFC 8291 from the receiving end, written separately from the code under test.</summary>
    public string Decrypt(byte[] message)
    {
        var salt = message[..16];
        Assert.Equal(4096u, BinaryPrimitives.ReadUInt32BigEndian(message.AsSpan(16, 4)));
        Assert.Equal(65, message[20]);
        var serverPublic = message[21..86];

        using var device = ECDiffieHellman.Create(_key);
        using var server = ECDiffieHellman.Create(WebPushEncryption.Parameters(serverPublic));
        var shared = device.DeriveRawSecretAgreement(server.PublicKey);

        var prkKey = Hmac(AuthSecret, shared);
        var ikm = Hmac(prkKey, [.. "WebPush: info\0"u8, .. PublicKey, .. serverPublic, 0x01]);
        var prk = Hmac(salt, ikm);
        var cek = Hmac(prk, [.. "Content-Encoding: aes128gcm\0"u8, 0x01])[..16];
        var nonce = Hmac(prk, [.. "Content-Encoding: nonce\0"u8, 0x01])[..12];

        var ciphertext = message[86..^16];
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(cek, 16);
        aes.Decrypt(nonce, ciphertext, message[^16..], plaintext);

        Assert.Equal(0x02, plaintext[^1]);
        return Encoding.UTF8.GetString(plaintext[..^1]);
    }

    private static byte[] Hmac(byte[] key, byte[] data) => HMACSHA256.HashData(key, data);
}
