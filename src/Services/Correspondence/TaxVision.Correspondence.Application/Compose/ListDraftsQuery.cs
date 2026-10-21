using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Application.Compose;

/// <param name="OwnerUserId">A1 — si viene, solo los borradores de ese usuario (ver
/// <c>IDraftRepository.ListOpenByCustomerAsync</c>).</param>
public sealed record ListDraftsQuery(
    Guid TenantId,
    DraftCustomerScope Customer,
    int Page,
    int Size,
    Guid? OwnerUserId = null
);
