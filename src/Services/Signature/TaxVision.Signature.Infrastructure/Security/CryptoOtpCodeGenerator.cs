using System.Security.Cryptography;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Infrastructure.Security;

/// <summary>
/// Genera OTPs numéricos con RNG criptográfico. Usa el rango uniforme
/// <see cref="RandomNumberGenerator.GetInt32(int, int)"/> para evitar sesgo modular.
/// </summary>
public sealed class CryptoOtpCodeGenerator : IOtpCodeGenerator
{
    public string Generate(int length)
    {
        if (length is < 4 or > 10)
            throw new ArgumentOutOfRangeException(nameof(length), "OTP length must be between 4 and 10 digits.");

        var digits = new char[length];
        for (var i = 0; i < length; i++)
            digits[i] = (char)('0' + RandomNumberGenerator.GetInt32(0, 10));
        return new string(digits);
    }
}
