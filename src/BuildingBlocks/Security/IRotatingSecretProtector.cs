namespace BuildingBlocks.Security;

/// <summary>
/// Cifrado AES-GCM con soporte de 2 master keys activas (current + previous) para rotación sin
/// downtime — a diferencia de <see cref="ISecretProtector"/> (un solo string opaco, sin versión).
/// Trabaja con las partes crudas del cifrado (ciphertext/nonce/tag/keyVersion) en vez de un string
/// serializado, para que el caller pueda persistir cada componente en su propia columna.
/// </summary>
public interface IRotatingSecretProtector
{
    RotatingProtectedSecret Protect(string plaintext, short? keyVersion = null);

    string Unprotect(RotatingProtectedSecret secret);
}

public readonly record struct RotatingProtectedSecret(byte[] Ciphertext, byte[] Nonce, byte[] Tag, short KeyVersion);
