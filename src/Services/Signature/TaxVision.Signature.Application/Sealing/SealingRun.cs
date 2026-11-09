using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Sealing;

/// <summary>Lo que lleva hecho una corrida de la fase A; la solicitud sigue intacta hasta la fase B.</summary>
public sealed class SealingRun
{
    private readonly List<SealedDocumentResult> _documents = [];
    private readonly List<SealedCertificateResult> _certificates = [];
    private readonly List<SaveFileRequestedIntegrationEvent> _fileRequests = [];

    public SealedDocumentResult? Find(Guid documentId) =>
        _documents.Find(document => document.DocumentId == documentId);

    public void Add(SealedDocumentResult document, SaveFileRequestedIntegrationEvent fileRequest)
    {
        _documents.Add(document);
        _fileRequests.Add(fileRequest);
    }

    public void Add(SealedCertificateResult certificate, SaveFileRequestedIntegrationEvent fileRequest)
    {
        _certificates.Add(certificate);
        _fileRequests.Add(fileRequest);
    }

    public PersistSealedDocumentsCommand ToCommand(SignatureRequest request, string correlationId, Error? failure) =>
        new(
            request.TenantId,
            request.Id,
            correlationId,
            _documents,
            _certificates,
            _fileRequests,
            failure?.Code,
            failure?.Message
        );
}
