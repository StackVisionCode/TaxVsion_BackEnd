namespace TaxVision.Signature.Domain.Audit;

/// <summary>
/// Tipo semántico del evento en la cadena de audit. La lista es cerrada y estable —
/// cambios en nombres/valores requieren migración explícita porque los verificadores
/// externos recomputan sobre el nombre serializado.
/// </summary>
public enum SignatureAuditEventKind
{
    RequestCreated,
    RequestSent,
    SignerViewed,

    /// <summary>F5 — El firmante vio el documento original (no solo el enlace). Semántica distinta a SignerViewed.</summary>
    DocumentViewed,
    ConsentAccepted,
    PinVerified,
    PinFailed,
    ChallengeIssued,
    ChallengeVerified,
    ChallengeFailed,
    DocumentSigned,
    SignerRejected,
    RequestCanceled,
    RequestExpired,
    RequestCompleted,
    RequestSealed,
    PreparerSigned,

    /// <summary>F7 — copia inmediata entregada a un firmante tras su firma.</summary>
    PartialCopyDelivered,

    /// <summary>F7 — fallo irrecuperable al entregar la copia parcial (no bloquea el flujo).</summary>
    PartialCopyFailed,

    /// <summary>
    /// F9 — El preparador reemplazó el PDF original de un documento del borrador. Si cambió el
    /// número de páginas, el payload reporta cuántos campos quedaron invalidados.
    /// </summary>
    DocumentReplaced,

    /// <summary>Entrega de documentos firmados emitida a un firmante (enlace propio de Signature, sin share).</summary>
    DeliveryIssued,

    /// <summary>Enlace de entrega rotado: lo pidió el firmante con el enlace vencido o lo reenvió la oficina.</summary>
    DeliveryReissued,

    /// <summary>La oficina revocó una entrega: su enlace deja de funcionar.</summary>
    DeliveryRevoked,
}
