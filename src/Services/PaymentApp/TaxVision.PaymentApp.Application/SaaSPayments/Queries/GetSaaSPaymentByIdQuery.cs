namespace TaxVision.PaymentApp.Application.SaaSPayments.Queries;

public sealed record GetSaaSPaymentByIdQuery(Guid TenantId, Guid SaaSPaymentId);
