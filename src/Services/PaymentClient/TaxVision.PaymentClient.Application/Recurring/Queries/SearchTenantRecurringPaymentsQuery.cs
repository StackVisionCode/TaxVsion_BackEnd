using TaxVision.PaymentClient.Domain.Recurring;

namespace TaxVision.PaymentClient.Application.Recurring.Queries;

public sealed record SearchTenantRecurringPaymentsQuery(Guid TenantId, Guid? TaxpayerId, RecurringStatus? Status, int Page, int PageSize);
