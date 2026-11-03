namespace TaxVision.Signature.Application.Sealing;

/// <summary>
/// Claves de idempotencia de los eventos del sellado. Un re-sellado (tras un rechazo de CloudStorage)
/// es un hecho distinto: lleva el FileId del archivo nuevo para que nadie lo descarte como duplicado,
/// mientras que el reintento o redelivery del mismo sellado conserva la misma clave.
/// </summary>
public static class SignatureSealingIdempotencyKeys
{
    public static string DocumentSealed(Guid signatureRequestId, Guid documentId, Guid sealedFileId) =>
        $"signature.doc_sealed:{signatureRequestId:N}:{documentId:N}:{sealedFileId:N}:v2";
}
