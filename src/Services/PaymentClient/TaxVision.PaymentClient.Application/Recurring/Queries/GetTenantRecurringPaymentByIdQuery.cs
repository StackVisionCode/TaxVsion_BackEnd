namespace TaxVision.PaymentClient.Application.Recurring.Queries;

public sealed record GetTenantRecurringPaymentByIdQuery(Guid TenantId, Guid TenantRecurringPaymentId);
