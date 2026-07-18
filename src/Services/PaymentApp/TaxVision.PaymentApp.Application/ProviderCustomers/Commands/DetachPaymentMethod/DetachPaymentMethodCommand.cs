namespace TaxVision.PaymentApp.Application.ProviderCustomers.Commands.DetachPaymentMethod;

public sealed record DetachPaymentMethodCommand(Guid TenantId, Guid TenantProviderCustomerId, Guid PaymentMethodId, Guid ActorUserId);
