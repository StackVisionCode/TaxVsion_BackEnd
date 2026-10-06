namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// F7 — la copia parcial ya está rendeada y subida a CloudStorage. Notification lo consume y
/// manda el correo/SMS/WhatsApp al firmante con el link de descarga del archivo.
/// </summary>
public sealed record SignerPartialCopyReadyIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required Guid SignerId { get; init; }
    public required Guid PartialCopyFileId { get; init; }
    public required string DocumentTitle { get; init; }
    public required string SignerEmail { get; init; }
    public required string SignerFullName { get; init; }
    public string? PhoneE164 { get; init; }
    public required string Language { get; init; }

    // F7 — token del share-link de descarga; Notification lo convierte a URL pública con el host del
    // tenant. Null si no se pudo crear el share.
    public string? ShareToken { get; init; }

    /// <summary>F7 — cantidad total de firmantes del request; permite adaptar la copy del email cuando hay un solo signer.</summary>
    public required int TotalSignersCount { get; init; }

    /// <summary>F7 — true si el preparador también va a enviar el PDF sellado; el template evita prometerlo cuando es false.</summary>
    public required bool SendSealedToSigners { get; init; }
}
