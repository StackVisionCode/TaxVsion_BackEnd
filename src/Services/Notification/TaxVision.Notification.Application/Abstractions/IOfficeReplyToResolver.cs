namespace TaxVision.Notification.Application.Abstractions;

/// <summary>
/// A quién responde un correo de la oficina que sale por el SMTP del sistema (DM-02): el Owner de la oficina.
/// Null si no se conoce su correo.
/// </summary>
public interface IOfficeReplyToResolver
{
    Task<string?> ResolveAsync(Guid tenantId, CancellationToken ct = default);
}
