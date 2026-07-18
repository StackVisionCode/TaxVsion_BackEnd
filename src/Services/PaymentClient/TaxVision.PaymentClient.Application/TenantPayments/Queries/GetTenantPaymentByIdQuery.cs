namespace TaxVision.PaymentClient.Application.TenantPayments.Queries;

public sealed record GetTenantPaymentByIdQuery(Guid TenantId, Guid TenantPaymentId);
