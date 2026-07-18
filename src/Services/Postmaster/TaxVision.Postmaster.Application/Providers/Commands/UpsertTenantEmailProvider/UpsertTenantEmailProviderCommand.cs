using TaxVision.Postmaster.Domain.Providers;

namespace TaxVision.Postmaster.Application.Providers.Commands.UpsertTenantEmailProvider;

/// <summary>Upsert por (TenantId, ProviderCode) — crea si no existe, reconfigura si ya existe.</summary>
public sealed record UpsertTenantEmailProviderCommand(
    Guid TenantId,
    Guid ActingUserId,
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
