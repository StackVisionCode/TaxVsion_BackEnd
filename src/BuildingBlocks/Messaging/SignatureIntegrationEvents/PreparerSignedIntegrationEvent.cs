namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// El preparer firmó internamente la solicitud (Form 8879 §V y similares). No es un
/// firmante externo — es el usuario staff autenticado del tenant. Se emite para audit
/// y para actualizar el estado en el dashboard.
/// </summary>
public sealed record PreparerSignedIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required Guid PreparerUserId { get; init; }
    public required string PtinOrEfin { get; init; }
    public required string PreparerDisplayName { get; init; }
    public required DateTime SignedAtUtc { get; init; }
}
