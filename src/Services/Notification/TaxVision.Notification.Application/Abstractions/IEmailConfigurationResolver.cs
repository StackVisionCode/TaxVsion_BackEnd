using TaxVision.Notification.Domain.Emailing.Configurations;

namespace TaxVision.Notification.Application.Abstractions;

/// <summary>
/// Configuración de correo resuelta y lista para enviar, con los secretos ya descifrados.
/// Uso interno de los senders/handlers; NUNCA se serializa en una respuesta HTTP.
/// </summary>
public sealed record ResolvedEmailConfiguration(
    Guid ConfigurationId,
    ProviderScope Scope,
    EmailProviderType ProviderType,
    string FromEmail,
    string? FromName,
    string? Host,
    int? Port,
    string? Username,
    string? Password,
    bool UseSsl,
    string? ApiKey,
    string? ClientId,
    string? ClientSecret,
    string? TenantProviderId
);

public interface IEmailConfigurationResolver
{
    /// <summary>
    /// Resuelve la configuración efectiva para un tenant: su default activa y, si no tiene,
    /// la default global del SaaS. Devuelve null si no hay ninguna configuración utilizable.
    /// </summary>
    Task<ResolvedEmailConfiguration?> ResolveAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Resuelve una configuración concreta por id (p. ej. para el endpoint de test).</summary>
    Task<ResolvedEmailConfiguration?> ResolveByIdAsync(Guid id, Guid? tenantId, CancellationToken ct = default);
}
