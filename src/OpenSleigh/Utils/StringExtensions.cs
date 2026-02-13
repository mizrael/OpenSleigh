using System.Security.Cryptography;
using System.Text;

namespace OpenSleigh.Utils;

internal static class StringExtensions
{
    public static string ComputeSha256Hash(this string rawData)
    {
        using var sha256Hash = SHA256.Create();
        Span<byte> hashBytes = stackalloc byte[32];

        if (!sha256Hash.TryComputeHash(Encoding.UTF8.GetBytes(rawData), hashBytes, out _))
            throw new CryptographicException("Failed to compute hash");

        Span<char> hexChars = stackalloc char[64];
        for (int i = 0, j = 0; i < hashBytes.Length; i++)
        {
            var b = hashBytes[i];
            hexChars[j++] = GetHexChar(b >> 4);
            hexChars[j++] = GetHexChar(b & 0xF);
        }

        return new string(hexChars);
    }

    private static char GetHexChar(int value)
    => (char)(value < 10 ? '0' + value : 'a' + (value - 10));
}