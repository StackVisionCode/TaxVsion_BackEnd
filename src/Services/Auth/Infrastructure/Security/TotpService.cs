using System.Security.Cryptography;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Infrastructure.Security;

/// <summary>
/// TOTP según RFC 6238 (HMAC-SHA1, paso de 30 s, 6 dígitos, ventana ±1 paso),
/// compatible con Google Authenticator, Microsoft Authenticator, Authy, 1Password, etc.
/// Implementación propia para no depender de paquetes externos.
/// </summary>
public sealed class TotpService : ITotpService
{
    private const int StepSeconds = 30;
    private const int Digits = 6;
    private const int WindowSteps = 1;
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public string GenerateSecret() => Base32Encode(RandomNumberGenerator.GetBytes(20));

    public string BuildOtpAuthUri(string accountName, string base32Secret, string issuer)
    {
        var label = Uri.EscapeDataString($"{issuer}:{accountName}");
        var escapedIssuer = Uri.EscapeDataString(issuer);
        return $"otpauth://totp/{label}?secret={base32Secret}&issuer={escapedIssuer}"
            + $"&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
    }

    public bool ValidateCode(string base32Secret, string code, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length != Digits)
            return false;

        byte[] key;
        try
        {
            key = Base32Decode(base32Secret);
        }
        catch (FormatException)
        {
            return false;
        }

        var timestep = (long)(utcNow - DateTime.UnixEpoch).TotalSeconds / StepSeconds;
        var normalized = code.Trim();

        for (var offset = -WindowSteps; offset <= WindowSteps; offset++)
        {
            var expected = ComputeCode(key, timestep + offset);
            if (
                CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.ASCII.GetBytes(expected),
                    System.Text.Encoding.ASCII.GetBytes(normalized)
                )
            )
            {
                return true;
            }
        }

        return false;
    }

    private static string ComputeCode(byte[] key, long timestep)
    {
        Span<byte> counter = stackalloc byte[8];
        for (var i = 7; i >= 0; i--)
        {
            counter[i] = (byte)(timestep & 0xFF);
            timestep >>= 8;
        }

        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counter.ToArray());

        var dynamicOffset = hash[^1] & 0x0F;
        var binaryCode =
            ((hash[dynamicOffset] & 0x7F) << 24)
            | ((hash[dynamicOffset + 1] & 0xFF) << 16)
            | ((hash[dynamicOffset + 2] & 0xFF) << 8)
            | (hash[dynamicOffset + 3] & 0xFF);

        return (binaryCode % 1_000_000).ToString("D6");
    }

    private static string Base32Encode(byte[] data)
    {
        var output = new System.Text.StringBuilder((data.Length + 4) / 5 * 8);
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                output.Append(Base32Alphabet[(buffer >> (bitsLeft - 5)) & 0x1F]);
                bitsLeft -= 5;
            }
        }

        if (bitsLeft > 0)
            output.Append(Base32Alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);

        return output.ToString();
    }

    private static byte[] Base32Decode(string base32)
    {
        var normalized = base32.Trim().TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>(normalized.Length * 5 / 8);
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var character in normalized)
        {
            var index = Base32Alphabet.IndexOf(character);
            if (index < 0)
                throw new FormatException("Invalid Base32 character.");

            buffer = (buffer << 5) | index;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                output.Add((byte)((buffer >> (bitsLeft - 8)) & 0xFF));
                bitsLeft -= 8;
            }
        }

        return [.. output];
    }
}
