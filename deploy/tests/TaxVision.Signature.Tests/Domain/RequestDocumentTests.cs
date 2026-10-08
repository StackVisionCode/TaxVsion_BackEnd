using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Tests.Domain;

public sealed class RequestDocumentTests
{
    [Fact]
    public void AddDocument_adds_document_with_next_order()
    {
        var request = NewDraft();

        var result = request.AddDocument(Guid.NewGuid(), "Supporting document", "W-2 2025");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Order);
        Assert.Equal("Supporting document", result.Value.Title);
        Assert.Equal("W-2 2025", result.Value.Note);
    }

    [Fact]
    public void RemoveDocument_removes_its_signer_and_preparer_fields()
    {
        var request = NewDraft();
        var document = request.Documents.Single();
        var signer = AddSigner(request);
        var position = Position();
        request.PlaceField(signer.Id, document.Id, SignatureFieldKind.Signature, position, null, true);
        request.PlacePreparerField(document.Id, SignatureFieldKind.Signature, position, null);

        var result = request.RemoveDocument(document.Id);

        Assert.True(result.IsSuccess);
        Assert.Empty(request.Documents);
        Assert.Empty(signer.Fields);
        Assert.Empty(request.PreparerFields);
    }

    [Fact]
    public void ReorderDocuments_applies_complete_order()
    {
        var request = NewDraft();
        var first = request.Documents.Single();
        var second = request.AddDocument(Guid.NewGuid(), "Second").Value;

        var result = request.ReorderDocuments([second.Id, first.Id]);

        Assert.True(result.IsSuccess);
        Assert.Equal([second.Id, first.Id], request.Documents.Select(document => document.Id));
        Assert.Equal([1, 2], request.Documents.Select(document => document.Order));
    }

    [Fact]
    public void ReplaceDocumentFile_invalidates_pre_hash()
    {
        var request = NewDraft();
        var document = request.Documents.Single();
        request.AttachDocumentHash(document.Id, Hash('a'));

        var result = request.ReplaceDocumentFile(document.Id, Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.Null(document.DocumentHashPre);
    }

    [Fact]
    public void Send_rejects_request_without_documents()
    {
        var request = NewDraft();
        request.RemoveDocument(request.Documents.Single().Id);
        AddSigner(request);

        var result = request.Send(DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.NoDocuments", result.Error.Code);
    }

    private static SignatureRequest NewDraft() =>
        SignatureRequest
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

    private static Signer AddSigner(SignatureRequest request) =>
        request
            .AddSigner(SignerEmail.Create("signer@example.com").Value, SignerFullName.Create("Signer One").Value, null)
            .Value;

    private static FieldPosition Position() => FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value;

    private static DocumentHash Hash(char value) => DocumentHash.Create(new string(value, 64)).Value;
}
