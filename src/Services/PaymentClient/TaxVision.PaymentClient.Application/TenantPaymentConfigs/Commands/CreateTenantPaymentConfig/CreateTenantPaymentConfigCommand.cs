using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.CreateTenantPaymentConfig;

public sealed record CreateTenantPaymentConfigCommand(
    Guid TenantId,
    PaymentProviderCode ProviderCode,
    TenantPaymentMode Mode,
    string PublishableKey,
    string StatementDescriptor,
    Guid ActorUserId
);
