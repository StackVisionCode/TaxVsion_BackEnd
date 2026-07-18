using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.TenantPayments.Commands.ProcessTenantWebhook;

public sealed record ProcessTenantWebhookCommand(
    Guid TenantId,
    PaymentProviderCode ProviderCode,
    string RawPayload,
    string SignatureHeader
);
