using TaxVision.Postmaster.Domain.Providers;

namespace TaxVision.Postmaster.Api.Requests;

/// <summary>Cuerpo del upsert del proveedor "default" de plataforma (PlatformAdmin-only).</summary>
public sealed record UpsertSystemEmailProviderRequest(
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
