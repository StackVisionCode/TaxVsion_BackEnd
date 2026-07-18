namespace BuildingBlocks.Infrastructure.Security;

/// <summary>
/// Config del <see cref="AesGcmRotatingSecretProtector"/>, sección "Encryption". Comparte
/// <c>MasterKey</c> con <see cref="AesGcmSecretProtector"/> (mismo valor, misma env var
/// ENCRYPTION_MASTER_KEY en todos los servicios que la usan) — la rotación agrega los campos
/// opcionales de "previous key" encima, sin romper compatibilidad con el protector simple.
/// </summary>
public sealed class RotatingSecretProtectionOptions
{
    public string MasterKey { get; set; } = string.Empty;
    public short MasterKeyVersion { get; set; } = 1;
    public string? PreviousMasterKey { get; set; }
    public short? PreviousMasterKeyVersion { get; set; }
}
