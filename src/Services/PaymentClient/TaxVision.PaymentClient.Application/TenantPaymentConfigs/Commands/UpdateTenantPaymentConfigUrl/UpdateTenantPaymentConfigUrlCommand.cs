using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.UpdateTenantPaymentConfigUrl;

/// <summary>Edita la URL/endpoint del proveedor de un config existente. <c>ApiBaseUrl</c> null o vacío
/// la limpia (vuelve al default del adapter).</summary>
public sealed record UpdateTenantPaymentConfigUrlCommand(
    Guid TenantId,
    PaymentProviderCode ProviderCode,
    string? ApiBaseUrl,
    Guid ActorUserId
);
