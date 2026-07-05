using TaxVision.Notification.Domain.Emailing.Configurations;

namespace TaxVision.Notification.Application.Email.Configurations;

/// <summary>
/// Proyección segura de una configuración de proveedor: expone metadatos y <b>flags</b> de presencia
/// de secretos (<c>HasPassword</c>…), pero NUNCA el cipher ni el valor en claro.
/// </summary>
public sealed record EmailConfigurationResponse(
    Guid Id,
    string Scope,
    Guid? TenantId,
    string ProviderType,
    string DisplayName,
    string FromEmail,
    string? FromName,
    string? Host,
    int? Port,
    string? Username,
    string? ClientId,
    string? TenantProviderId,
    bool UseSsl,
    bool HasPassword,
    bool HasApiKey,
    bool HasClientSecret,
    bool IsDefault,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc
);

public static class EmailConfigurationMapper
{
    public static EmailConfigurationResponse ToResponse(EmailProviderConfiguration c) =>
        new(
            c.Id,
            c.Scope.ToString(),
            c.TenantId,
            c.ProviderType.ToString(),
            c.DisplayName,
            c.FromEmail,
            c.FromName,
            c.Host,
            c.Port,
            c.Username,
            c.ClientId,
            c.TenantProviderId,
            c.UseSsl,
            !string.IsNullOrEmpty(c.PasswordCipher),
            !string.IsNullOrEmpty(c.ApiKeyCipher),
            !string.IsNullOrEmpty(c.ClientSecretCipher),
            c.IsDefault,
            c.IsActive,
            c.CreatedAtUtc,
            c.UpdatedAtUtc
        );
}
