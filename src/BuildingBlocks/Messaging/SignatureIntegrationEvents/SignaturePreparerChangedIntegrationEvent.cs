namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// El preparador (Form 8879) de una solicitud ya enviada pasó a otra persona antes de firmar: por el
/// retiro de un empleado (sistema, con el sucesor) o a mano por un admin con <c>signature.request.manage</c>.
/// El cambio ya quedó en la cadena de auditoría de la solicitud. Sirve para avisar al nuevo preparador de
/// que tiene una firma pendiente.
/// </summary>
public sealed record SignaturePreparerChangedIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required string Title { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public Guid? PreviousPreparerUserId { get; init; }
    public required Guid NewPreparerUserId { get; init; }
    public required string NewPreparerDisplayName { get; init; }

    /// <summary>Quién lo cambió; null = el sistema (retiro del empleado).</summary>
    public Guid? ChangedByUserId { get; init; }

    /// <summary>Ver <see cref="SignaturePreparerChangeReasons"/>.</summary>
    public required string Reason { get; init; }
    public required DateTime ChangedAtUtc { get; init; }
}

/// <summary>Motivos de <see cref="SignaturePreparerChangedIntegrationEvent.Reason"/>.</summary>
public static class SignaturePreparerChangeReasons
{
    public const string Offboarding = "Offboarding";
    public const string Manual = "Manual";
}
