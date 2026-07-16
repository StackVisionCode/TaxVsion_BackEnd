namespace TaxVision.PaymentClient.Application.Recurring.Commands.ResumeTenantRecurringPayment;

public sealed record ResumeTenantRecurringPaymentCommand(Guid TenantId, Guid TenantRecurringPaymentId, Guid ActorUserId);
