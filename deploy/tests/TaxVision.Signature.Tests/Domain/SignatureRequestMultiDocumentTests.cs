using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Tests.Domain;

public sealed class SignatureRequestMultiDocumentTests
{
    [Fact]
    public void Send_requires_each_signer_to_participate_in_a_document()
    {
        var request = NewDraftWithTwoHashedDocuments();
        var participant = AddSigner(request, "participant@example.com", "Participant One");
        AddSigner(request, "ghost@example.com", "Ghost Signer");
        PlaceSignature(request, participant.Id, request.Documents[0].Id);
        PlaceSignature(request, participant.Id, request.Documents[1].Id);

        var result = request.Send(DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.SignerWithoutDocument", result.Error.Code);
    }

    [Fact]
    public void Send_requires_a_signature_field_on_every_document()
    {
        var request = NewDraftWithTwoHashedDocuments();
        var signer = AddSigner(request, "signer@example.com", "Signer One");
        PlaceSignature(request, signer.Id, request.Documents[0].Id);

        var result = request.Send(DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.NoSignatureField", result.Error.Code);
    }

    [Fact]
    public void Signer_completion_emits_one_domain_event_per_participating_document()
    {
        var request = NewReadyRequest();
        var signer = request.Signers.Single();
        request.Send(DateTime.UtcNow);

        var result = request.MarkSignerSigned(signer.Id, DateTime.UtcNow, null, null);

        Assert.True(result.IsSuccess);
        var events = request.DomainEvents.OfType<SignerCompletedDocument>().ToList();
        Assert.Equal(2, events.Count);
        Assert.Equal(
            request.Documents.Select(document => document.Id).Order(),
            events.Select(e => e.DocumentId).Order()
        );
    }

    [Fact]
    public void Signer_can_complete_documents_across_multiple_sessions()
    {
        var request = NewReadyRequest();
        var signer = request.Signers.Single();
        var firstDocumentId = request.Documents[0].Id;
        var secondDocumentId = request.Documents[1].Id;
        request.Send(DateTime.UtcNow);

        var first = request.MarkSignerDocumentsCompleted(
            signer.Id,
            [firstDocumentId],
            DateTime.UtcNow,
            SignatureCaptureMethod.Typed,
            signer.FullName.Value,
            null,
            null,
            null
        );

        Assert.True(first.IsSuccess);
        Assert.Equal(SignerStatus.InProgress, signer.Status);
        Assert.Single(signer.DocumentCompletions);
        Assert.Equal(firstDocumentId, signer.DocumentCompletions.Single().DocumentId);
        Assert.Equal(SignatureRequestStatus.InProgress, request.Status);

        var second = request.MarkSignerDocumentsCompleted(
            signer.Id,
            [secondDocumentId],
            DateTime.UtcNow.AddDays(1),
            SignatureCaptureMethod.Typed,
            signer.FullName.Value,
            null,
            null,
            null
        );

        Assert.True(second.IsSuccess);
        Assert.Equal(SignerStatus.Signed, signer.Status);
        Assert.Equal(2, signer.DocumentCompletions.Count);
        Assert.Equal(SignatureRequestStatus.Completed, request.Status);
    }

    [Fact]
    public void Completing_the_same_document_is_idempotent()
    {
        var request = NewReadyRequest();
        var signer = request.Signers.Single();
        var documentId = request.Documents[0].Id;
        request.Send(DateTime.UtcNow);
        var first = CompleteDocument(request, signer, documentId);

        var duplicate = CompleteDocument(request, signer, documentId);

        Assert.True(first.IsSuccess);
        Assert.True(duplicate.IsSuccess);
        Assert.Empty(duplicate.Value);
        Assert.Single(signer.DocumentCompletions);
        Assert.Single(request.DomainEvents.OfType<SignerCompletedDocument>());
    }

    [Fact]
    public void Ready_document_can_be_sealed_while_request_remains_in_progress()
    {
        var request = NewDraftWithTwoHashedDocuments();
        var firstSigner = AddSigner(request, "first@example.com", "First Signer");
        var secondSigner = AddSigner(request, "second@example.com", "Second Signer");
        var firstDocument = request.Documents[0];
        var secondDocument = request.Documents[1];
        PlaceSignature(request, firstSigner.Id, firstDocument.Id);
        PlaceSignature(request, secondSigner.Id, secondDocument.Id);
        request.Send(DateTime.UtcNow);

        var completed = CompleteDocument(request, firstSigner, firstDocument.Id);
        var sealedResult = request.MarkDocumentSealed(firstDocument.Id, Guid.NewGuid(), Hash('c'), DateTime.UtcNow);

        Assert.True(completed.IsSuccess);
        Assert.True(request.IsDocumentReadyForSealing(firstDocument.Id));
        Assert.False(request.IsDocumentReadyForSealing(secondDocument.Id));
        Assert.Equal(SignatureRequestStatus.InProgress, request.Status);
        Assert.True(sealedResult.IsSuccess);
        Assert.NotNull(firstDocument.SealedFileId);
        Assert.Null(secondDocument.SealedFileId);
    }

    [Fact]
    public void Certificate_requires_every_document_to_be_sealed()
    {
        var request = NewReadyRequest();
        var signer = request.Signers.Single();
        request.Send(DateTime.UtcNow);
        request.MarkSignerSigned(signer.Id, DateTime.UtcNow, null, null);
        request.MarkDocumentSealed(request.Documents[0].Id, Guid.NewGuid(), Hash('c'), DateTime.UtcNow);

        var early = request.RecordCertificate(Guid.NewGuid());
        request.MarkDocumentSealed(request.Documents[1].Id, Guid.NewGuid(), Hash('d'), DateTime.UtcNow);
        var completed = request.RecordCertificate(Guid.NewGuid());

        Assert.True(early.IsFailure);
        Assert.Equal("Signature.Request.DocumentsNotSealed", early.Error.Code);
        Assert.True(completed.IsSuccess);
        Assert.True(request.AllDocumentsSealed());
    }

    private static SignatureRequest NewReadyRequest()
    {
        var request = NewDraftWithTwoHashedDocuments();
        var signer = AddSigner(request, "signer@example.com", "Signer One");
        foreach (var document in request.Documents)
            PlaceSignature(request, signer.Id, document.Id);
        return request;
    }

    private static SignatureRequest NewDraftWithTwoHashedDocuments()
    {
        var request = SignatureRequest
            .CreateDraft(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Multi-document request",
                null,
                "Fiscal",
                Guid.NewGuid(),
                72,
                false,
                false,
                true
            )
            .Value;
        request.AddDocument(Guid.NewGuid(), "Second document");
        request.AttachDocumentHash(request.Documents[0].Id, Hash('a'));
        request.AttachDocumentHash(request.Documents[1].Id, Hash('b'));
        return request;
    }

    private static Signer AddSigner(SignatureRequest request, string email, string name) =>
        request.AddSigner(SignerEmail.Create(email).Value, SignerFullName.Create(name).Value, null).Value;

    private static void PlaceSignature(SignatureRequest request, Guid signerId, Guid documentId) =>
        request.PlaceField(
            signerId,
            documentId,
            SignatureFieldKind.Signature,
            FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value,
            null,
            true
        );

    private static BuildingBlocks.Results.Result<IReadOnlyList<Guid>> CompleteDocument(
        SignatureRequest request,
        Signer signer,
        Guid documentId
    ) =>
        request.MarkSignerDocumentsCompleted(
            signer.Id,
            [documentId],
            DateTime.UtcNow,
            SignatureCaptureMethod.Typed,
            signer.FullName.Value,
            null,
            null,
            null
        );

    private static DocumentHash Hash(char value) => DocumentHash.Create(new string(value, 64)).Value;
}
