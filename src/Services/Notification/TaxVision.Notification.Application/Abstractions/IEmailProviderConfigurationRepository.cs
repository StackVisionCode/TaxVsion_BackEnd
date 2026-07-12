using TaxVision.Notification.Domain.Emailing.Configurations;

namespace TaxVision.Notification.Application.Abstractions;

public interface IEmailProviderConfigurationRepository
{
    Task AddAsync(EmailProviderConfiguration configuration, CancellationToken ct = default);

    /// <summary>
    /// Obtiene por id restringido a lo que el tenant puede ver: su propia configuración o una System.
    /// Si <paramref name="tenantId"/> es null (contexto PlatformAdmin sin tenant) devuelve cualquiera.
    /// </summary>
    Task<EmailProviderConfiguration?> GetByIdAsync(Guid id, Guid? tenantId, CancellationToken ct = default);

    /// <summary>Configuraciones visibles: las del tenant y, si <paramref name="includeSystem"/>, las globales.</summary>
    Task<IReadOnlyList<EmailProviderConfiguration>> ListAsync(
        Guid? tenantId,
        bool includeSystem,
        CancellationToken ct = default
    );

    /// <summary>Configuración default activa del tenant (Scope=Tenant, IsDefault, IsActive).</summary>
    Task<EmailProviderConfiguration?> GetTenantDefaultAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Configuración default activa global (Scope=System, IsDefault, IsActive).</summary>
    Task<EmailProviderConfiguration?> GetSystemDefaultAsync(CancellationToken ct = default);

    /// <summary>Quita el flag <c>IsDefault</c> de todas las configuraciones del mismo scope/tenant.</summary>
    Task ClearDefaultsAsync(ProviderScope scope, Guid? tenantId, CancellationToken ct = default);
}
