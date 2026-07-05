using BuildingBlocks.Security;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing.Configurations;

namespace TaxVision.Notification.Infrastructure.Email;

/// <summary>
/// Resuelve la configuración de correo efectiva (default del tenant → default global) y descifra
/// los secretos con <see cref="ISecretProtector"/>. Devuelve un tipo interno con secretos en claro;
/// nunca se serializa en responses.
/// </summary>
public sealed class EmailConfigurationResolver(
    IEmailProviderConfigurationRepository repository,
    ISecretProtector protector
) : IEmailConfigurationResolver
{
    public async Task<ResolvedEmailConfiguration?> ResolveAsync(Guid tenantId, CancellationToken ct = default)
    {
        var config =
            await repository.GetTenantDefaultAsync(tenantId, ct) ?? await repository.GetSystemDefaultAsync(ct);
        return config is null ? null : Decrypt(config);
    }

    public async Task<ResolvedEmailConfiguration?> ResolveByIdAsync(
        Guid id,
        Guid? tenantId,
        CancellationToken ct = default
    )
    {
        var config = await repository.GetByIdAsync(id, tenantId, ct);
        return config is null ? null : Decrypt(config);
    }

    private ResolvedEmailConfiguration Decrypt(EmailProviderConfiguration c) =>
        new(
            c.Id,
            c.Scope,
            c.ProviderType,
            c.FromEmail,
            c.FromName,
            c.Host,
            c.Port,
            c.Username,
            Reveal(c.PasswordCipher),
            c.UseSsl,
            Reveal(c.ApiKeyCipher),
            c.ClientId,
            Reveal(c.ClientSecretCipher),
            c.TenantProviderId
        );

    private string? Reveal(string? cipher) =>
        string.IsNullOrEmpty(cipher) ? null : protector.Unprotect(cipher);
}
