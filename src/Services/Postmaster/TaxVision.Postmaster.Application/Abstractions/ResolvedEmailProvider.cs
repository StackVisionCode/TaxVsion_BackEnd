namespace TaxVision.Postmaster.Application.Abstractions;

/// <summary>
/// Snapshot inmutable de la configuración de conexión de un provider ya resuelto, lista para usar
/// por <see cref="IEmailSender"/>. El password llega descifrado (la resolución vive en Infrastructure,
/// que es la única capa autorizada a invocar <c>ISecretProtector.Unprotect</c>).
/// </summary>
public sealed record ResolvedEmailProvider(
    string ProviderCode,
    string Host,
    int Port,
    bool UseTls,
    string? Username,
    string? Password,
    string FromAddress,
    string? FromDisplayName,
    int RateLimitPerMinute
);
