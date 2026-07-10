using System.Security.Cryptography;
using System.Text;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Infrastructure.Security;

/// <summary>
/// Implementación por defecto de <see cref="IPinHasher"/> con PBKDF2-HMAC-SHA256.
///
/// <para>
/// Formato serializado del hash: <c>v1$iterations$saltB64$hashB64</c>. La versión
/// permite migrar iteraciones a futuro (bumping) sin romper hashes existentes.
/// </para>
///
/// <para>
/// Iteraciones: 210_000 (recomendación OWASP 2024 para PBKDF2-SHA256). El PIN es corto
/// por naturaleza; PBKDF2 con este nivel de iteración es suficiente para deshabilitar
/// ataques offline razonables — reforzado además por el lockout a los 5 intentos que
/// impone el aggregate.
/// </para>
/// </summary>
public sealed class Pbkdf2PinHasher : IPinHasher
{
    private const string CurrentVersion = "v1";
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 210_000;

    public string Hash(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = DeriveKey(plaintext, salt, Iterations);
        return Serialize(Iterations, salt, hash);
    }

    public bool Verify(string plaintext, string hash)
    {
        if (string.IsNullOrEmpty(plaintext) || string.IsNullOrEmpty(hash))
            return false;

        var parsed = TryParse(hash);
        if (parsed is null)
            return false;

        var candidate = DeriveKey(plaintext, parsed.Value.Salt, parsed.Value.Iterations);
        return CryptographicOperations.FixedTimeEquals(candidate, parsed.Value.Hash);
    }

    // ------------------------------------------------------------------
    // Métodos privados: cada uno una única responsabilidad
    // ------------------------------------------------------------------

    private static byte[] DeriveKey(string plaintext, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(
            password: Encoding.UTF8.GetBytes(plaintext),
            salt: salt,
            iterations: iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: HashSize
        );

    private static string Serialize(int iterations, byte[] salt, byte[] hash) =>
        $"{CurrentVersion}${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";

    private static (int Iterations, byte[] Salt, byte[] Hash)? TryParse(string hash)
    {
        var parts = hash.Split('$');
        if (parts.Length != 4 || parts[0] != CurrentVersion)
            return null;

        if (!int.TryParse(parts[1], out var iterations) || iterations < 1000)
            return null;

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var stored = Convert.FromBase64String(parts[3]);
            if (salt.Length != SaltSize || stored.Length != HashSize)
                return null;
            return (iterations, salt, stored);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
