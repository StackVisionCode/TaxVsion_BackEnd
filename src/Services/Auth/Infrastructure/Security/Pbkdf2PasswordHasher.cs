using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Infrastructure.Security;

/// <summary>
/// PBKDF2-SHA256. Los hashes nuevos se hacen sobre la forma NFKC (03 §10); para no dejar afuera a los viejos, la
/// verificación prueba la forma NFKC y, si difiere, también el texto tal como llega.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = KeyDerivation.Pbkdf2(
            password.Normalize(NormalizationForm.FormKC),
            salt,
            KeyDerivationPrf.HMACSHA256,
            Iterations,
            KeySize
        );

        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hash))
            return false;

        var normalized = password.Normalize(NormalizationForm.FormKC);
        return Matches(normalized, hash) || (normalized != password && Matches(password, hash));
    }

    private static bool Matches(string password, string hash)
    {
        try
        {
            var parts = hash.Split('.');
            if (parts.Length != 2)
                return false;

            var salt = Convert.FromBase64String(parts[0]);
            var expected = Convert.FromBase64String(parts[1]);
            if (salt.Length != SaltSize || expected.Length != KeySize)
                return false;

            var actual = KeyDerivation.Pbkdf2(password, salt, KeyDerivationPrf.HMACSHA256, Iterations, KeySize);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
