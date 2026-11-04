using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// Signature emitió (o reemitió) una entrega de documentos firmados a UN firmante. Notification manda un
/// solo correo con todos sus archivos y el enlace a la página de entrega <c>/d/{token}</c>. El token viaja
/// cifrado en <see cref="ProtectedToken"/> (el exchange es fanout); no es un share de CloudStorage.
/// </summary>
public sealed record SignedDocumentsDeliveryReadyIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required Guid DeliveryId { get; init; }

    /// <summary><c>Completed</c> (paquete final) o <c>PartialCopy</c> (copia en curso tras firmar).</summary>
    public required string Purpose { get; init; }

    public required SignerContactSnapshot Signer { get; init; }
    public required IReadOnlyList<DeliveryItemDescriptor> Items { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }

    /// <summary>Token plano del enlace, cifrado con el esquema <see cref="SignatureDeliveryPayloadSchemas.DeliveryLink"/>.</summary>
    public required ProtectedPayload ProtectedToken { get; init; }

    /// <summary>Los archivos también quedaron en la carpeta del cliente en el portal (un único cliente firmante).</summary>
    public bool AvailableInClientPortal { get; init; }

    /// <summary>Archivos a adjuntar al correo (vacío si la oficina no adjunta o no caben bajo su tope).</summary>
    public IReadOnlyList<Guid> AttachmentFileIds { get; init; } = [];

    /// <summary>Reemisión (enlace nuevo) en vez de la primera entrega: el correo lo dice.</summary>
    public bool IsReissue { get; init; }

    /// <summary>La oficina permite pedir un enlace nuevo desde la página cuando este vence.</summary>
    public bool CanRequestNewLink { get; init; }

    public DateTime CompletedAtUtc { get; init; }
    public int TotalSignersCount { get; init; }

    /// <summary>Solo copia parcial: si al terminar la solicitud el firmante recibirá además el documento sellado.</summary>
    public bool SendSealedToSigners { get; init; }
}

/// <summary>Un archivo de la entrega. <see cref="Kind"/>: <c>SignedDocument</c>, <c>Certificate</c> o <c>PartialCopy</c>.</summary>
public sealed record DeliveryItemDescriptor(Guid ItemId, string Title, string Kind);

/// <summary>Contenido cifrado del enlace de entrega.</summary>
public sealed record SignatureDeliveryLinkPayload(string Token);

public static class SignatureDeliveryPayloadSchemas
{
    public const string DeliveryLink = "signature.delivery_link.v1";

    /// <summary>Atadura del payload: el tenant y la entrega, para que no se pueda reusar en otra.</summary>
    public static ProtectedPayloadBinding BindingFor(Guid tenantId, Guid deliveryId) =>
        new(tenantId, null, null, IdempotencyKey: $"signature.delivery:{deliveryId:N}");
}
