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
}
