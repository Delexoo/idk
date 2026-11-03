using System.Security.Cryptography;
using System.Text;

namespace Idk.Services;

/// <summary>
/// Opens the model key sealed into this build. The plaintext is not stored in settings or the environment.
/// </summary>
internal static class SealedApi
{
    private static readonly byte[] AesKey =
    [
        0x7F, 0xFD, 0xFC, 0x7F, 0x78, 0x36, 0x03, 0x3C,
        0x3C, 0x56, 0xB1, 0xB0, 0xE6, 0x82, 0xEC, 0xEA,
        0xFB, 0x3E, 0xC0, 0x20, 0xA7, 0x78, 0xF6, 0x18,
        0x57, 0xBD, 0x7A, 0xEE, 0xCE, 0x4B, 0xFF, 0x65
    ];

    private static readonly byte[] MacKey =
    [
        0x33, 0x3C, 0xDE, 0xDD, 0x61, 0x90, 0xC8, 0x32,
        0x25, 0x02, 0x70, 0x51, 0xB1, 0x1C, 0xEA, 0x44,
        0x87, 0x77, 0xC0, 0x00, 0x56, 0x76, 0x48, 0x13,
        0xE3, 0x31, 0x60, 0x32, 0x5A, 0xAC, 0xBB, 0xA1
    ];

    internal static bool IsReady => EmbeddedOpenRouter.Blob.Length > 48;

    internal static string? Open()
    {
        var blob = EmbeddedOpenRouter.Blob;
        if (blob.Length <= 48) return null;

        var iv = blob.AsSpan(0, 16).ToArray();
        var tag = blob.AsSpan(16, 32).ToArray();
        var cipher = blob.AsSpan(48).ToArray();
        var signed = new byte[iv.Length + cipher.Length];
        Buffer.BlockCopy(iv, 0, signed, 0, iv.Length);
        Buffer.BlockCopy(cipher, 0, signed, iv.Length, cipher.Length);

        using var hmac = new HMACSHA256(MacKey);
        var expected = hmac.ComputeHash(signed);
        if (!CryptographicOperations.FixedTimeEquals(expected, tag))
            return null;

        using var aes = Aes.Create();
        aes.Key = AesKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        byte[] plain;
        using (var decryptor = aes.CreateDecryptor())
            plain = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);

        var text = Encoding.UTF8.GetString(plain);
        CryptographicOperations.ZeroMemory(plain);
        CryptographicOperations.ZeroMemory(cipher);
        CryptographicOperations.ZeroMemory(signed);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
