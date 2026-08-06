using TaxVision.Customer.Domain.Customers;

namespace TaxVision.Customer.Application.Customers;

/// <summary>
/// DTO plano para la reconciliación de proyecciones de customer en otros microservicios
/// (Signature/Communication/Notes/Correspondence). A diferencia de <see cref="CustomerSummaryResponse"/>,
/// incluye el <see cref="TenantId"/> porque la lista de reconciliación es cross-tenant (la pagina
/// un token de la PlatformTenant vía <c>GET customers/internal/reconciliation</c>) y cada consumidor
/// necesita la clave (TenantId, CustomerId) para hacer el upsert en su read-model local.
/// </summary>
public sealed record CustomerReconciliationResponse(
    Guid TenantId,
    Guid CustomerId,
    string DisplayName,
    string PrimaryEmail,
    CustomerStatus Status
);
