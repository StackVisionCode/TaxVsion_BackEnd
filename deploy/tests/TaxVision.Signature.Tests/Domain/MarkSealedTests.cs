using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Tests.Domain;

public sealed class MarkSealedTests
{
    [Fact]
    public void Mark_document_sealed_fails_when_document_is_not_ready()
    {
        var request = NewInProgressRequest();

        var result = request.MarkDocumentSealed(
            request.Documents.Single().Id,
            Guid.NewGuid(),
            Hash('a'),
            DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.DocumentNotReady", result.Error.Code);
    }

    [Fact]
    public void Mark_document_sealed_stores_file_hash_and_timestamp()
    {
        var request = NewCompletedRequest();
        var document = request.Documents.Single();
        var sealedFileId = Guid.NewGuid();
        var sealedAtUtc = DateTime.UtcNow;

        var result = request.MarkDocumentSealed(document.Id, sealedFileId, Hash('b'), sealedAtUtc);

        Assert.True(result.IsSuccess);
        Assert.Equal(sealedFileId, document.SealedFileId);
        Assert.Equal(new string('b', 64), document.DocumentHashPost!.Value);
        Assert.Equal(sealedAtUtc, document.SealedAtUtc);
        Assert.True(request.AllDocumentsSealed());
    }

    [Fact]
    public void Mark_document_sealed_is_idempotent_for_same_file_and_hash()
    {
        var request = NewCompletedRequest();
        var document = request.Documents.Single();
        var sealedFileId = Guid.NewGuid();
        var hash = Hash('c');

        var first = request.MarkDocumentSealed(document.Id, sealedFileId, hash, DateTime.UtcNow);
        var second = request.MarkDocumentSealed(document.Id, sealedFileId, hash, DateTime.UtcNow.AddMinutes(1));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
    }

    [Fact]
    public void Mark_document_sealed_rejects_empty_file_id()
    {
        var request = NewCompletedRequest();

        var result = request.MarkDocumentSealed(request.Documents.Single().Id, Guid.Empty, Hash('d'), DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Document.SealedFile", result.Error.Code);
    }

    [Fact]
    public void Mark_document_sealed_rejects_unknown_document()
    {
        var request = NewCompletedRequest();

        var result = request.MarkDocumentSealed(Guid.NewGuid(), Guid.NewGuid(), Hash('e'), DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.DocumentMissing", result.Error.Code);
    }

    [Fact]
    public void Record_certificate_rejects_empty_file_id_after_all_documents_are_sealed()
    {
        var request = NewCompletedRequest();
        request.MarkDocumentSealed(request.Documents.Single().Id, Guid.NewGuid(), Hash('f'), DateTime.UtcNow);

        var result = request.RecordCertificate(Guid.Empty);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.CertificateFile", result.Error.Code);
    }

    private static SignatureRequest NewCompletedRequest()
    {
        var request = NewInProgressRequest();
        var signer = request.Signers.Single();
        request.MarkSignerSigned(signer.Id, DateTime.UtcNow, null, null);
        return request;
    }

    private static SignatureRequest NewInProgressRequest()
    {
        var request = SignatureRequest
            .CreateDraft(Guid.NewGuid(), Guid.NewGuid(), "Test sealed", null, "Fiscal", 72, false, false, true)
            .Value;
        request.AddDocument(Guid.NewGuid(), "Primary document");

        var signer = request
            .AddSigner(SignerEmail.Create("s@example.com").Value, SignerFullName.Create("Some Name").Value, null)
            .Value;
        request.PlaceField(
            signer.Id,
            request.Documents.Single().Id,
            SignatureFieldKind.Signature,
            FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value,
            null,
            false
        );
        request.AttachDocumentHash(request.Documents.Single().Id, Hash('a'));
        request.Send(DateTime.UtcNow);
        return request;
    }

    private static DocumentHash Hash(char value) => DocumentHash.Create(new string(value, 64)).Value;
}
