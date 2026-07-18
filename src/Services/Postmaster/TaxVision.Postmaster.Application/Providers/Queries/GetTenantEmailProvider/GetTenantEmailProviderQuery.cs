namespace TaxVision.Postmaster.Application.Providers.Queries.GetTenantEmailProvider;

public sealed record GetTenantEmailProviderQuery(Guid TenantId, string ProviderCode);

/// <summary>Config del provider del tenant — nunca incluye el password, ni siquiera cifrado.</summary>
public sealed record TenantEmailProviderDto(
    Guid Id,
    string ProviderCode,
    string DisplayName,
    string ProviderType,
    string? Host,
    int? Port,
    bool UseTls,
    string? Username,
    string FromAddressDefault,
    string? FromDisplayNameDefault,
    int RateLimitPerMinute,
    bool Enabled,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc
);
