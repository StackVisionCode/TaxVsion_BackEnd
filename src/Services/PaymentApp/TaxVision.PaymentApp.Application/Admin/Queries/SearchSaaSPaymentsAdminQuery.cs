using TaxVision.PaymentApp.Domain.SaaSPayments;

namespace TaxVision.PaymentApp.Application.Admin.Queries;

/// <summary>
/// <paramref name="TenantId"/> nulo trae todos los tenants — solo alcanzable con el permiso
/// <c>payment_app.admin.cross_tenant</c> (§42.6 del diseño), nunca por el flujo normal
/// tenant-scoped.
/// </summary>
public sealed record SearchSaaSPaymentsAdminQuery(
    Guid? TenantId, PaymentStatus? Status, SaaSPaymentType? Type, DateTime? From, DateTime? To, int Page, int PageSize);
