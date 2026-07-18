using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Postmaster.Domain.ValueObjects;

namespace TaxVision.Postmaster.Domain.Providers;

/// <summary>
/// Configuración de proveedor de envío propia de un tenant (ej: su propio SMTP corporativo). A
/// diferencia de <see cref="SystemEmailProvider"/>, su ausencia NUNCA cae a system — es una política
/// estricta anti-spoofing (plan §14.5): si el tenant no configuró un provider, el envío falla como
/// <c>ProviderNotConfigured</c> en vez de usar credenciales de otro tenant.
/// </summary>
public sealed class TenantEmailProvider : TenantEntity
{
    private TenantEmailProvider() { }

    public string ProviderCode { get; private set; } = default!;
    public string DisplayName { get; private set; } = default!;
    public EmailProviderType ProviderType { get; private set; }
    public string? Host { get; private set; }
    public int? Port { get; private set; }
    public bool UseTls { get; private set; }
    public string? Username { get; private set; }
    public EncryptedSecret? PasswordCipher { get; private set; }
    public string FromAddressDefault { get; private set; } = default!;
    public string? FromDisplayNameDefault { get; private set; }
    public int RateLimitPerMinute { get; private set; }
    public bool Enabled { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public static Result<TenantEmailProvider> Create(
        Guid tenantId,
        string providerCode,
        string displayName,
        EmailProviderType providerType,
        string fromAddressDefault,
        string? fromDisplayNameDefault,
        string? host,
        int? port,
        bool useTls,
        string? username,
        string? passwordCipher,
        int rateLimitPerMinute,
        Guid createdByUserId,
        DateTime createdAtUtc
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<TenantEmailProvider>(new Error("TenantEmailProvider.Tenant", "Tenant is required."));

        var validationError = ValidateConnectionFields(
            providerCode,
            displayName,
            fromAddressDefault,
            providerType,
            host,
            rateLimitPerMinute
        );
        if (validationError is not null)
            return Result.Failure<TenantEmailProvider>(validationError);

        var secretResult = ToEncryptedSecret(passwordCipher);
        if (secretResult.IsFailure)
            return Result.Failure<TenantEmailProvider>(secretResult.Error);

        var provider = new TenantEmailProvider
        {
            Id = Guid.NewGuid(),
            ProviderCode = providerCode.Trim(),
            DisplayName = displayName.Trim(),
            ProviderType = providerType,
            FromAddressDefault = fromAddressDefault.Trim().ToLowerInvariant(),
            FromDisplayNameDefault = fromDisplayNameDefault,
            Host = host,
            Port = port,
            UseTls = useTls,
            Username = username,
            PasswordCipher = secretResult.Value,
            RateLimitPerMinute = rateLimitPerMinute,
            Enabled = true,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = createdAtUtc,
        };
        provider.SetTenant(tenantId);
        return Result.Success(provider);
    }

    /// <summary>Reconfigura los datos de conexión SMTP del tenant. Una sola acción de negocio coherente.</summary>
    public Result UpdateConnection(
        string? host,
        int? port,
        bool useTls,
        string? username,
        string? passwordCipher,
        string fromAddressDefault,
        string? fromDisplayNameDefault,
        int rateLimitPerMinute,
        DateTime updatedAtUtc
    )
    {
        var validationError = ValidateConnectionFields(
            ProviderCode,
            DisplayName,
            fromAddressDefault,
            ProviderType,
            host,
            rateLimitPerMinute
        );
        if (validationError is not null)
            return Result.Failure(validationError);

        var secretResult = ToEncryptedSecret(passwordCipher);
        if (secretResult.IsFailure)
            return Result.Failure(secretResult.Error);

        Host = host;
        Port = port;
        UseTls = useTls;
        Username = username;
        PasswordCipher = secretResult.Value;
        FromAddressDefault = fromAddressDefault.Trim().ToLowerInvariant();
        FromDisplayNameDefault = fromDisplayNameDefault;
        RateLimitPerMinute = rateLimitPerMinute;
        UpdatedAtUtc = updatedAtUtc;
        return Result.Success();
    }

    public void Enable(DateTime updatedAtUtc)
    {
        Enabled = true;
        UpdatedAtUtc = updatedAtUtc;
    }

    public void Disable(DateTime updatedAtUtc)
    {
        Enabled = false;
        UpdatedAtUtc = updatedAtUtc;
    }

    private static Error? ValidateConnectionFields(
        string providerCode,
        string displayName,
        string fromAddressDefault,
        EmailProviderType providerType,
        string? host,
        int rateLimitPerMinute
    )
    {
        if (string.IsNullOrWhiteSpace(providerCode) || providerCode.Length > 50)
            return new Error(
                "TenantEmailProvider.ProviderCode",
                "ProviderCode is required and must be at most 50 chars."
            );

        if (string.IsNullOrWhiteSpace(displayName))
            return new Error("TenantEmailProvider.DisplayName", "DisplayName is required.");

        if (string.IsNullOrWhiteSpace(fromAddressDefault) || !fromAddressDefault.Contains('@'))
            return new Error("TenantEmailProvider.FromAddressDefault", "A valid FromAddressDefault is required.");

        if (providerType == EmailProviderType.Smtp && string.IsNullOrWhiteSpace(host))
            return new Error("TenantEmailProvider.Host", "Host is required for Smtp provider type.");

        if (rateLimitPerMinute <= 0)
            return new Error("TenantEmailProvider.RateLimitPerMinute", "RateLimitPerMinute must be greater than zero.");

        return null;
    }

    private static Result<EncryptedSecret?> ToEncryptedSecret(string? cipher)
    {
        if (cipher is null)
            return Result.Success<EncryptedSecret?>(null);

        var result = EncryptedSecret.Create(cipher);
        return result.IsFailure
            ? Result.Failure<EncryptedSecret?>(result.Error)
            : Result.Success<EncryptedSecret?>(result.Value);
    }
}
