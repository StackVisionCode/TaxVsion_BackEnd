using TaxVision.Postmaster.Domain.Providers;

namespace TaxVision.Postmaster.Api.Requests;

/// <summary>Cuerpo compartido por POST (create) y PUT (update) — upsert full-replace.</summary>
public sealed record UpsertTenantEmailProviderRequest(
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
