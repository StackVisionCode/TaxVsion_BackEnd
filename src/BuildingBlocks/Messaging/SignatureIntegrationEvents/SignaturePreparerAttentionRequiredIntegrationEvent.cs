namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// Una solicitud de firma necesita que alguien de la oficina actúe por el retiro de un empleado. Signature
/// no decide el canal: el consumidor avisa a <see cref="RecipientUserId"/> o, si es null, a quienes tienen
/// <see cref="AudiencePermission"/> (admins de firma). Nunca se deja una solicitud bloqueada en silencio.
/// </summary>
public sealed record SignaturePreparerAttentionRequiredIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required string Title { get; init; }

    /// <summary>Estado de la solicitud al momento del aviso (nombre del enum de Signature).</summary>
    public required string Status { get; init; }

    /// <summary>Ver <see cref="SignaturePreparerAttentionReasons"/>.</summary>
    public required string Reason { get; init; }

    /// <summary>Detalle legible en inglés (por qué no se pudo resolver solo).</summary>
    public required string Detail { get; init; }

    /// <summary>El empleado retirado que originó el aviso.</summary>
    public required Guid OffboardedUserId { get; init; }
    public required Guid CreatedByUserId { get; init; }

    /// <summary>Destinatario concreto; null = la audiencia por permiso.</summary>
    public Guid? RecipientUserId { get; init; }

    /// <summary>Permiso de la audiencia cuando no hay destinatario concreto.</summary>
    public required string AudiencePermission { get; init; }
}

/// <summary>Motivos de <see cref="SignaturePreparerAttentionRequiredIntegrationEvent.Reason"/>.</summary>
public static class SignaturePreparerAttentionReasons
{
    /// <summary>Borrador: se quitó al preparador retirado; hay que elegir otro antes de enviar.</summary>
    public const string PreparerCleared = "PreparerCleared";

    /// <summary>Enviada sin firma del preparador y no se pudo pasar al sucesor: un admin debe cambiarlo.</summary>
    public const string PreparerChangeBlocked = "PreparerChangeBlocked";

    /// <summary>Sin enviar, del retirado y sin sucesor, que no se descartó (programada, legal hold u origen externo).</summary>
    public const string UnsentRequestKept = "UnsentRequestKept";
}
