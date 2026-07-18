namespace TaxVision.PaymentClient.Application.Recurring.Commands.PauseTenantRecurringPayment;

public sealed record PauseTenantRecurringPaymentCommand(Guid TenantId, Guid TenantRecurringPaymentId, Guid ActorUserId);
