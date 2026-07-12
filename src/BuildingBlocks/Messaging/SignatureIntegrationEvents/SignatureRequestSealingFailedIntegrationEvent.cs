namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// El sealing falló y ya se agotaron los reintentos configurados. Se emite para que
/// el dashboard staff pueda alertar y la operación humana intervenga (revisar el
/// documento original, re-encolar manualmente, etc.). La solicitud sigue en estado
/// <c>Completed</c> — no se rollback la firma; sólo el paso posterior falló.
/// </summary>
public sealed record SignatureRequestSealingFailedIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required string Reason { get; init; }
    public required string ErrorCode { get; init; }
    public required DateTime FailedAtUtc { get; init; }
}
