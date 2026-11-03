namespace TaxVision.Tenant.Application.Profiles.Abstractions;

/// <summary>Página del listado interno de Billing: <c>{ items, page, size, totalCount }</c>.</summary>
public sealed record BillingIssuerProfilePage(
    IReadOnlyList<BillingIssuerProfile> Items,
    int Page,
    int Size,
    int TotalCount
);
