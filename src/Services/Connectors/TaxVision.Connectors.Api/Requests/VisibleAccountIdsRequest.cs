namespace TaxVision.Connectors.Api.Requests;

/// <summary>M2M: buzones visibles para (TenantId, UserId). IncludeOffice=false → solo los personales del usuario.</summary>
public sealed record VisibleAccountIdsRequest(Guid TenantId, Guid UserId, bool IncludeOffice);
