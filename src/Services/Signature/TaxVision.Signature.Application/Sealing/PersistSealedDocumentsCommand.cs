using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;

namespace TaxVision.Signature.Application.Sealing;

/// <summary>Documento ya sellado y dejado en MinIO, a la espera de quedar registrado en la solicitud.</summary>
public sealed record SealedDocumentResult(Guid DocumentId, Guid SealedFileId, string HashPost, DateTime SealedAtUtc);

/// <summary>Certificado ya dejado en MinIO. Sin <c>DocumentId</c> es el de toda la solicitud.</summary>
public sealed record SealedCertificateResult(Guid? DocumentId, Guid CertificateFileId);

/// <summary>
/// SIG-6, fase B del sellado: lo que la fase A dejó en MinIO, sin bytes. Va por cola local durable, así que un fallo
/// al guardar reintenta solo esto y no vuelve a sellar ni a subir.
/// </summary>
public sealed record PersistSealedDocumentsCommand(
    Guid TenantId,
    Guid SignatureRequestId,
    string CorrelationId,
    IReadOnlyList<SealedDocumentResult> Documents,
    IReadOnlyList<SealedCertificateResult> Certificates,
    IReadOnlyList<SaveFileRequestedIntegrationEvent> FileRequests,
    string? FailureCode = null,
    string? FailureMessage = null
);
