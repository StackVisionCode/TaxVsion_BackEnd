namespace TaxVision.PaymentClient.Application.Recurring.Commands.CancelTenantRecurringPayment;

public sealed record CancelTenantRecurringPaymentCommand(
    Guid TenantId,
    Guid TenantRecurringPaymentId,
    string Reason,
    Guid ActorUserId
);
