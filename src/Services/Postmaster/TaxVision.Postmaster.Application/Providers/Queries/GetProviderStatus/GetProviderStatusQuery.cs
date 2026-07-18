namespace TaxVision.Postmaster.Application.Providers.Queries.GetProviderStatus;

public sealed record GetProviderStatusQuery(Guid TenantId);

public sealed record TenantProviderConfigSummary(string FromAddress, string? Host);

/// <summary>Resumen de estado para UI de configuración — nunca expone el password.</summary>
public sealed record ProviderStatusDto(
    bool HasSystemProvider,
    bool HasTenantProvider,
    bool TenantProviderHealthy,
    DateTime? LastCheckAtUtc,
    TenantProviderConfigSummary? TenantProviderConfig
);
