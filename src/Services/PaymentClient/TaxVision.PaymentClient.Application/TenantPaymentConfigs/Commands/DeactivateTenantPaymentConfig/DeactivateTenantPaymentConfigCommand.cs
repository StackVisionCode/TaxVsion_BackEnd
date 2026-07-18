using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.DeactivateTenantPaymentConfig;

public sealed record DeactivateTenantPaymentConfigCommand(
    Guid TenantId,
    PaymentProviderCode ProviderCode,
    string Reason,
    Guid ActorUserId
);
