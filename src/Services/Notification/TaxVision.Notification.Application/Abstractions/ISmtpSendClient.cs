using BuildingBlocks.Results;

namespace TaxVision.Notification.Application.Abstractions;

/// <summary>Parámetros de conexión SMTP ya resueltos (secretos descifrados).</summary>
public sealed record SmtpConnection(
    string Host,
    int Port,
    string? Username,
    string? Password,
    bool UseSsl,
    string FromEmail,
    string? FromName
);

/// <summary>
/// Envío SMTP de bajo nivel con parámetros de conexión explícitos (no de <c>SmtpOptions</c>).
/// Lo usan el envío por configuración resuelta y el endpoint de test.
/// </summary>
public interface ISmtpSendClient
{
    Task<Result> SendAsync(SmtpConnection connection, EmailMessage message, CancellationToken ct = default);
}
