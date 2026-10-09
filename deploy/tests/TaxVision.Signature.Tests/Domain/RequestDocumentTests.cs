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

        var result = request.ReplaceDocumentFile(document.Id, Guid.NewGuid(), newPageCount: null);

        Assert.True(result.IsSuccess);
        Assert.Null(document.DocumentHashPre);
    }

    // F9 — Mismo page count: las coordenadas normalizadas siguen válidas, los campos sobreviven y
    // el outcome reporta FieldsInvalidated = 0.
    [Fact]
    public void ReplaceDocumentFile_same_page_count_keeps_fields()
    {
        var request = NewDraft();
        var document = request.Documents.Single();
        // Primera llamada: establece PageCount=5 (OldPageCount era null, no invalida nada).
        request.ReplaceDocumentFile(document.Id, Guid.NewGuid(), newPageCount: 5);
        var signer = AddSigner(request);
        var position = Position();
        request.PlaceField(signer.Id, document.Id, SignatureFieldKind.Signature, position, null, true);
        request.PlacePreparerField(document.Id, SignatureFieldKind.Signature, position, null);

        var result = request.ReplaceDocumentFile(document.Id, Guid.NewGuid(), newPageCount: 5);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.FieldsInvalidated);
        Assert.Equal(5, result.Value.OldPageCount);
        Assert.Equal(5, result.Value.NewPageCount);
        Assert.Single(signer.Fields);
        Assert.Single(request.PreparerFields);
    }

    // F9 — Page count distinto: las coordenadas dejan de ser válidas; se eliminan los campos del
    // signer y del preparador para ESE documento (los de otros docs quedan intactos).
    [Fact]
    public void ReplaceDocumentFile_different_page_count_invalidates_fields_for_that_document_only()
    {
        var request = NewDraft();
        var first = request.Documents.Single();
        request.ReplaceDocumentFile(first.Id, Guid.NewGuid(), newPageCount: 3);
        var second = request.AddDocument(Guid.NewGuid(), "Second", null, pageCount: 2).Value;
        var signer = AddSigner(request);
        var position = Position();
        request.PlaceField(signer.Id, first.Id, SignatureFieldKind.Signature, position, null, true);
        request.PlaceField(signer.Id, first.Id, SignatureFieldKind.Initials, position, null, true);
        request.PlaceField(signer.Id, second.Id, SignatureFieldKind.Signature, position, null, true);
        request.PlacePreparerField(first.Id, SignatureFieldKind.Signature, position, null);

        var result = request.ReplaceDocumentFile(first.Id, Guid.NewGuid(), newPageCount: 10);

        Assert.True(result.IsSuccess);
        // 2 fields del signer + 1 del preparador = 3 descartados en el doc reemplazado.
        Assert.Equal(3, result.Value.FieldsInvalidated);
        Assert.Equal(3, result.Value.OldPageCount);
        Assert.Equal(10, result.Value.NewPageCount);
        Assert.Single(signer.Fields);
        Assert.Equal(second.Id, signer.Fields[0].DocumentId);
        Assert.Empty(request.PreparerFields);
    }

    // F9 — Dato ausente: si cualquiera de los dos page counts es null no podemos comparar, se
    // conserva por defecto (el autosave viejo que no manda pageCount sigue funcionando).
    [Fact]
    public void ReplaceDocumentFile_unknown_page_count_keeps_fields()
    {
        var request = NewDraft();
        var document = request.Documents.Single();
        var signer = AddSigner(request);
        var position = Position();
        request.PlaceField(signer.Id, document.Id, SignatureFieldKind.Signature, position, null, true);

        // Old PageCount = null (nunca se seteó al crear), new PageCount = 10 → sigue siendo
        // "no comparable" porque old es desconocido.
        var result = request.ReplaceDocumentFile(document.Id, Guid.NewGuid(), newPageCount: 10);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.FieldsInvalidated);
        Assert.Single(signer.Fields);
    }

    // F9 — Un reemplazo que apunta al fileId de OTRO doc en la misma request es una colisión; no
    // debe permitir dejar dos docs apuntando al mismo CloudStorage file.
    [Fact]
    public void ReplaceDocumentFile_rejects_duplicate_file_in_same_request()
    {
        var request = NewDraft();
        var first = request.Documents.Single();
        var secondFileId = Guid.NewGuid();
        request.AddDocument(secondFileId, "Second");

        var result = request.ReplaceDocumentFile(first.Id, secondFileId, newPageCount: 1);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.DuplicateDocument", result.Error.Code);
    }

    // F9 — La regla "solo en Draft" la hereda de EnsureCanBeEdited(); una vez enviada no se puede.
    [Fact]
    public void ReplaceDocumentFile_rejects_when_not_editable()
    {
        var request = NewDraft();
        var document = request.Documents.Single();
        var signer = AddSigner(request);
        request.PlaceField(signer.Id, document.Id, SignatureFieldKind.Signature, Position(), null, true);
        request.AttachDocumentHash(document.Id, Hash('a'));
        request.Send(DateTime.UtcNow);

        var result = request.ReplaceDocumentFile(document.Id, Guid.NewGuid(), newPageCount: 1);

        Assert.True(result.IsFailure);
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
