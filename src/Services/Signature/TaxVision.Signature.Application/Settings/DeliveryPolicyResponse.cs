namespace TaxVision.Signature.Application.Settings;

/// <summary>Entrega de los documentos firmados configurada por la oficina.</summary>
public sealed record DeliveryPolicyResponse(
    int LinkLifetimeDays,
    bool AllowPublicReissue,
    bool AttachDocuments,
    long MaxAttachmentBytes
);
