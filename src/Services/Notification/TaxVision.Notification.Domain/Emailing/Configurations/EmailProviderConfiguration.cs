using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Notification.Domain.Emailing.Configurations;

/// <summary>Tipo de proveedor de envío de correo.</summary>
public enum EmailProviderType
{
    Smtp,
    GmailApi,
    MicrosoftGraph,
    SendGrid,
    Mailgun,
    AmazonSes,
    Custom,
}

/// <summary>Ámbito de la configuración: global del SaaS (System) o propia del tenant.</summary>
public enum ProviderScope
{
    System,
    Tenant,
}

/// <summary>
/// Configuración de un proveedor de envío de correo, a nivel SaaS (<see cref="ProviderScope.System"/>,
/// <see cref="TenantId"/> nulo) o por tenant. Los secretos (contraseña SMTP, API key, client secret)
/// se guardan cifrados con <c>ISecretProtector</c>; nunca en claro ni expuestos en responses.
/// </summary>
/// <remarks>
/// Deriva de <see cref="BaseEntity"/> (no de <c>TenantEntity</c>) porque las configuraciones globales
/// tienen <see cref="TenantId"/> nulo, y <c>TenantEntity.TenantId</c> es no-nullable.
/// </remarks>
public sealed class EmailProviderConfiguration : BaseEntity
{
    private EmailProviderConfiguration() { }

    public Guid? TenantId { get; private set; }
    public ProviderScope Scope { get; private set; }
    public EmailProviderType ProviderType { get; private set; }
    public string DisplayName { get; private set; } = default!;

    // SMTP
    public string? Host { get; private set; }
    public int? Port { get; private set; }
    public string? Username { get; private set; }
    public string? PasswordCipher { get; private set; }
    public bool UseSsl { get; private set; }

    // Proveedores por API
    public string? ApiKeyCipher { get; private set; }
    public string? ClientId { get; private set; }
    public string? ClientSecretCipher { get; private set; }
    public string? TenantProviderId { get; private set; }

    // Remitente
    public string FromEmail { get; private set; } = default!;
    public string? FromName { get; private set; }

    public bool IsDefault { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Crea una configuración. Los parámetros <c>*Cipher</c> ya vienen cifrados por la capa de
    /// aplicación (el dominio no conoce el mecanismo de cifrado).
    /// </summary>
    public static Result<EmailProviderConfiguration> Create(
        ProviderScope scope,
        Guid? tenantId,
        EmailProviderType providerType,
        string displayName,
        string fromEmail,
        string? fromName,
        string? host,
        int? port,
        string? username,
        string? passwordCipher,
        bool useSsl,
        string? apiKeyCipher,
        string? clientId,
        string? clientSecretCipher,
        string? tenantProviderId,
        bool isDefault
    )
    {
        if (scope == ProviderScope.Tenant && (tenantId is null || tenantId == Guid.Empty))
            return Result.Failure<EmailProviderConfiguration>(
                new Error("EmailConfiguration.Tenant", "Tenant configurations require a tenant id.")
            );

        if (scope == ProviderScope.System && tenantId is not null)
            return Result.Failure<EmailProviderConfiguration>(
                new Error("EmailConfiguration.Scope", "System configurations must not carry a tenant id.")
            );

        var validation = ValidateCore(displayName, fromEmail, providerType, host);
        if (validation.IsFailure)
            return Result.Failure<EmailProviderConfiguration>(validation.Error);

        var config = new EmailProviderConfiguration
        {
            Id = Guid.NewGuid(),
            Scope = scope,
            TenantId = scope == ProviderScope.Tenant ? tenantId : null,
            ProviderType = providerType,
            DisplayName = displayName.Trim(),
            FromEmail = fromEmail.Trim(),
            FromName = Normalize(fromName),
            Host = Normalize(host),
            Port = port,
            Username = Normalize(username),
            PasswordCipher = passwordCipher,
            UseSsl = useSsl,
            ApiKeyCipher = apiKeyCipher,
            ClientId = Normalize(clientId),
            ClientSecretCipher = clientSecretCipher,
            TenantProviderId = Normalize(tenantProviderId),
            IsDefault = isDefault,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
        };
        return Result.Success(config);
    }

    /// <summary>
    /// Actualiza datos no-secretos y, opcionalmente, secretos. Un <c>*Cipher</c> nulo significa
    /// "conservar el valor actual" (permite editar sin reenviar la contraseña/api key).
    /// </summary>
    public Result Update(
        string displayName,
        string fromEmail,
        string? fromName,
        string? host,
        int? port,
        string? username,
        string? passwordCipher,
        bool useSsl,
        string? apiKeyCipher,
        string? clientId,
        string? clientSecretCipher,
        string? tenantProviderId
    )
    {
        var validation = ValidateCore(displayName, fromEmail, ProviderType, host);
        if (validation.IsFailure)
            return validation;

        DisplayName = displayName.Trim();
        FromEmail = fromEmail.Trim();
        FromName = Normalize(fromName);
        Host = Normalize(host);
        Port = port;
        Username = Normalize(username);
        if (passwordCipher is not null)
            PasswordCipher = passwordCipher;
        UseSsl = useSsl;
        if (apiKeyCipher is not null)
            ApiKeyCipher = apiKeyCipher;
        ClientId = Normalize(clientId);
        if (clientSecretCipher is not null)
            ClientSecretCipher = clientSecretCipher;
        TenantProviderId = Normalize(tenantProviderId);
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    public void SetAsDefault()
    {
        IsDefault = true;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void UnsetDefault()
    {
        IsDefault = false;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Activate()
    {
        IsActive = true;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        IsDefault = false;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static Result ValidateCore(
        string displayName,
        string fromEmail,
        EmailProviderType providerType,
        string? host
    )
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return Result.Failure(new Error("EmailConfiguration.DisplayName", "Display name is required."));

        if (string.IsNullOrWhiteSpace(fromEmail))
            return Result.Failure(new Error("EmailConfiguration.FromEmail", "From email is required."));

        if (providerType == EmailProviderType.Smtp && string.IsNullOrWhiteSpace(host))
            return Result.Failure(new Error("EmailConfiguration.Host", "SMTP configurations require a host."));

        return Result.Success();
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
