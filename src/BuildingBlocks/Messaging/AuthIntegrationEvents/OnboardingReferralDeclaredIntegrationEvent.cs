using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// El comprador de un alta escribió (o llegó con) un código de referido. Growth registra la atribución —el código SOLO
/// identifica quién recomendó, no cambia el precio— y avisa al referidor. Por evento: Auth no llama a Growth por HTTP
/// para esto ni necesita el Id de la atribución; cuando el alta paga, Growth califica por el <see cref="OnboardingId"/>.
/// Idempotente por onboarding: reentregar o reintentar el checkout no duplica nada. Un código inválido o revocado se
/// descarta en Growth (el referido paga igual el precio completo); nunca bloquea el alta.
/// </summary>
[MessageIdentity("auth.onboarding.referral_declared.v1")]
public sealed record OnboardingReferralDeclaredIntegrationEvent : IntegrationEvent
{
    public required Guid OnboardingId { get; init; }
    public required string ReferralCode { get; init; }

    /// <summary>Canal por el que llegó el enlace (?ch=), si se conoce.</summary>
    public string? Channel { get; init; }

    /// <summary>Envío del código (?sid=) que originó el enlace, si se conoce.</summary>
    public Guid? ShareId { get; init; }

    public override string ToString() =>
        $"{nameof(OnboardingReferralDeclaredIntegrationEvent)} {{ OnboardingId = {OnboardingId}, ReferralCode = <redacted> }}";
}
