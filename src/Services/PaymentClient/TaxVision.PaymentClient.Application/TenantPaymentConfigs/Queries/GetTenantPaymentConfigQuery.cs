using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.TenantPaymentConfigs.Queries;

public sealed record GetTenantPaymentConfigQuery(Guid TenantId, PaymentProviderCode ProviderCode);
