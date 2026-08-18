namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// Publicado por Auth al crear o reenviar un EmailVerificationChallenge de signup pago-primero
/// (PayFlow_Implementation_Plan.md §Fase 5). TenantId queda en Guid.Empty — el proceso es
/// pre-tenant, igual que SaaSPaymentType.OnboardingInitial (plan §3.3).
/// </summary>
public sealed record OnboardingOtpRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid ChallengeId { get; init; }
    public required string Email { get; init; }

    /// <summary>Código OTP en claro para entrega. No se persiste en claro en ningún servicio.</summary>
    public required string OtpCode { get; init; }

    public required DateTime ExpiresAtUtc { get; init; }
    public string? FirstNameHint { get; init; }
}
