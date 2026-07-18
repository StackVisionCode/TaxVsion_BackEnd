using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Postmaster.Domain.ValueObjects;

namespace TaxVision.Postmaster.Domain.Providers;

public enum EmailProviderType
{
    Smtp,
    GmailSend,
    GraphSend,
}

/// <summary>
/// Configuración global de un proveedor de envío disponible a todos los tenants (ej: SMTP relay
/// corporativo). Deriva de <see cref="BaseEntity"/> (no de <c>TenantEntity</c>) porque es cross-tenant
/// — mismo criterio que <c>EmailProviderConfiguration</c> en Notification para el scope System.
/// </summary>
/// <remarks>
/// El password ya llega cifrado (<paramref name="passwordCipher"/> en <see cref="Create"/>) — el
/// dominio no conoce el mecanismo de cifrado, esa responsabilidad vive en Application/Infrastructure
/// vía <c>ISecretProtector</c>.
/// </remarks>
public sealed class SystemEmailProvider : BaseEntity
{
    private SystemEmailProvider() { }

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
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public static Result<SystemEmailProvider> Create(
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
        DateTime createdAtUtc
    )
    {
        if (string.IsNullOrWhiteSpace(providerCode) || providerCode.Length > 50)
            return Result.Failure<SystemEmailProvider>(
                new Error("SystemEmailProvider.ProviderCode", "ProviderCode is required and must be at most 50 chars.")
            );

        if (string.IsNullOrWhiteSpace(displayName))
            return Result.Failure<SystemEmailProvider>(
                new Error("SystemEmailProvider.DisplayName", "DisplayName is required.")
            );

        if (string.IsNullOrWhiteSpace(fromAddressDefault) || !fromAddressDefault.Contains('@'))
            return Result.Failure<SystemEmailProvider>(
                new Error("SystemEmailProvider.FromAddressDefault", "A valid FromAddressDefault is required.")
            );

        if (providerType == EmailProviderType.Smtp && string.IsNullOrWhiteSpace(host))
            return Result.Failure<SystemEmailProvider>(
                new Error("SystemEmailProvider.Host", "Host is required for Smtp provider type.")
            );

        if (rateLimitPerMinute <= 0)
            return Result.Failure<SystemEmailProvider>(
                new Error("SystemEmailProvider.RateLimitPerMinute", "RateLimitPerMinute must be greater than zero.")
            );

        var secretResult = ToEncryptedSecret(passwordCipher);
        if (secretResult.IsFailure)
            return Result.Failure<SystemEmailProvider>(secretResult.Error);

        var provider = new SystemEmailProvider
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
            CreatedAtUtc = createdAtUtc,
        };
        return Result.Success(provider);
    }

    /// <summary>Reconfigura los datos de conexión SMTP. Una sola acción de negocio coherente.</summary>
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
        if (ProviderType == EmailProviderType.Smtp && string.IsNullOrWhiteSpace(host))
            return Result.Failure(new Error("SystemEmailProvider.Host", "Host is required for Smtp provider type."));

        if (string.IsNullOrWhiteSpace(fromAddressDefault) || !fromAddressDefault.Contains('@'))
            return Result.Failure(
                new Error("SystemEmailProvider.FromAddressDefault", "A valid FromAddressDefault is required.")
            );

        if (rateLimitPerMinute <= 0)
            return Result.Failure(
                new Error("SystemEmailProvider.RateLimitPerMinute", "RateLimitPerMinute must be greater than zero.")
            );

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

    private static Result<EncryptedSecret?> ToEncryptedSecret(string? cipher)
    {
        if (cipher is null)
            return Result.Success<EncryptedSecret?>(null);

        var result = EncryptedSecret.Create(cipher);
        return result.IsFailure
            ? Result.Failure<EncryptedSecret?>(result.Error)
            : Result.Success<EncryptedSecret?>(result.Value);
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
}
