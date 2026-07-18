using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.UpdateTenantPaymentConfigSecrets;

/// <summary>
/// <see cref="SecretKey"/> y <see cref="WebhookSecret"/> viajan en texto plano SOLO en este
/// comando, en memoria, por el tiempo mínimo hasta que el handler los cifra vía
/// <c>ISecretProtector</c> — nunca se loguean ni se persisten sin cifrar.
/// </summary>
public sealed record UpdateTenantPaymentConfigSecretsCommand(
    Guid TenantId,
    PaymentProviderCode ProviderCode,
    string SecretKey,
    string WebhookSecret,
    Guid ActorUserId
);
