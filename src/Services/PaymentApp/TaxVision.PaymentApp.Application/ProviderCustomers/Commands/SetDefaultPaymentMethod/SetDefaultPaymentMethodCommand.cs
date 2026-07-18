namespace TaxVision.PaymentApp.Application.ProviderCustomers.Commands.SetDefaultPaymentMethod;

public sealed record SetDefaultPaymentMethodCommand(
    Guid TenantId,
    Guid TenantProviderCustomerId,
    Guid PaymentMethodId,
    Guid ActorUserId
);
