using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Aperture.Web.Features.Notifications;

/// <summary>
/// Encrypts a notification for one device, as Web Push requires (RFC 8291, "aes128gcm"): only the device can read
/// it, not the push service (Apple, Google, Mozilla) that delivers it. Each message uses a fresh key pair and salt.
/// </summary>
public static class WebPushEncryption
{
    /// <summary>Push services accept up to 4096 bytes per message, so everything goes in one record of that size.</summary>
    public const int RecordSize = 4096;

    private const int TagSize = 16;
    private const int HeaderSize = 16 + 4 + 1 + 65;

    /// <summary>The largest payload that fits in 4096 bytes, next to the header, the padding delimiter and the AES-GCM tag.</summary>
    public const int MaxPayload = RecordSize - HeaderSize - TagSize - 1;

    /// <param name="userAgentPublicKey">The subscription's p256dh key: an uncompressed P-256 point (65 bytes).</param>
    /// <param name="authSecret">The subscription's auth secret (16 bytes).</param>
    public static byte[] Encrypt(ReadOnlySpan<byte> payload, byte[] userAgentPublicKey, byte[] authSecret)
    {
        using var serverKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return Encrypt(payload, userAgentPublicKey, authSecret, serverKey, RandomNumberGenerator.GetBytes(16));
    }

    /// <summary>With the server's key pair and the salt given, for the RFC's example.</summary>
    public static byte[] Encrypt(
        ReadOnlySpan<byte> payload, byte[] userAgentPublicKey, byte[] authSecret, ECDiffieHellman serverKey, byte[] salt)
    {
        if (payload.Length > MaxPayload)
        {
            throw new ArgumentException($"A push message holds at most {MaxPayload} bytes.", nameof(payload));
        }
        if (authSecret.Length != 16)
        {
            throw new ArgumentException("The auth secret must be 16 bytes.", nameof(authSecret));
        }

        var serverPublicKey = Point(serverKey.ExportParameters(includePrivateParameters: false));
        using var userAgentKey = ECDiffieHellman.Create(Parameters(userAgentPublicKey));
        var sharedSecret = serverKey.DeriveRawSecretAgreement(userAgentKey.PublicKey);

        // Combine the shared secret with the auth secret, then derive the content key and nonce (RFC 8291 section 3.4).
        byte[] keyInfo = [.. "WebPush: info\0"u8, .. userAgentPublicKey, .. serverPublicKey];
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 32, authSecret, keyInfo);
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        var contentKey = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, "Content-Encoding: aes128gcm\0"u8.ToArray());
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, "Content-Encoding: nonce\0"u8.ToArray());

        // One record: the payload and the delimiter 0x02 ("last record"), no further padding.
        byte[] plaintext = [.. payload, 0x02];

        var message = new byte[HeaderSize + plaintext.Length + TagSize];
        var header = message.AsSpan(0, HeaderSize);
        salt.CopyTo(header);
        BinaryPrimitives.WriteUInt32BigEndian(header[16..], RecordSize);
        header[20] = (byte)serverPublicKey.Length;
        serverPublicKey.CopyTo(header[21..]);

        using var aes = new AesGcm(contentKey, TagSize);
        aes.Encrypt(nonce, plaintext, message.AsSpan(HeaderSize, plaintext.Length), message.AsSpan(HeaderSize + plaintext.Length));
        return message;
    }

    /// <summary>Checks that a browser's key is a P-256 point before it's stored.</summary>
    public static bool IsValidPublicKey(byte[] key)
    {
        try
        {
            using var _ = ECDiffieHellman.Create(Parameters(key));
            return true;
        }
        catch (Exception e) when (e is ArgumentException or CryptographicException)
        {
            return false;
        }
    }

    /// <summary>An uncompressed point: 0x04, then X and Y (32 bytes each).</summary>
    public static byte[] Point(ECParameters parameters) => [0x04, .. parameters.Q.X!, .. parameters.Q.Y!];

    public static ECParameters Parameters(byte[] point, byte[]? privateKey = null)
    {
        if (point is not [0x04, ..] || point.Length != 65)
        {
            throw new ArgumentException("Expected an uncompressed P-256 public key (65 bytes).", nameof(point));
        }

        return new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = point[1..33], Y = point[33..] },
            D = privateKey,
        };
    }
}
