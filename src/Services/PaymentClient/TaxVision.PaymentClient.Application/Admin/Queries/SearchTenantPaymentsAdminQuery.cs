using TaxVision.PaymentClient.Domain.TenantPayments;

namespace TaxVision.PaymentClient.Application.Admin.Queries;

/// <summary>
/// <paramref name="TenantId"/> nulo trae todos los tenants — solo alcanzable con el permiso
/// <c>payment_client.admin.cross_tenant</c> (§42.6 del diseño).
/// </summary>
public sealed record SearchTenantPaymentsAdminQuery(Guid? TenantId, PaymentStatus? Status, DateTime? From, DateTime? To, int Page, int PageSize);
