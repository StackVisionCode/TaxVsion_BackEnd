using TaxVision.Postmaster.Domain.Providers;

namespace TaxVision.Postmaster.Application.Providers.Commands.UpsertSystemEmailProvider;

/// <summary>Upsert por ProviderCode — crea si no existe, reconfigura si ya existe. Solo PlatformAdmin.</summary>
public sealed record UpsertSystemEmailProviderCommand(
    string ProviderCode,
    string DisplayName,
    EmailProviderType ProviderType,
    string FromAddressDefault,
    string? FromDisplayNameDefault,
    string? Host,
    int? Port,
    bool UseTls,
    string? Username,
    string? Password,
    int RateLimitPerMinute
);
