using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Tests.Domain;

/// <summary>
/// Solo un documento que el servidor revisó y aceptó se puede enviar: uno rechazado bloquea el envío con su
/// motivo, el número de páginas válido es el que contó el servidor y un campo no puede caer fuera del PDF.
/// </summary>
public sealed class DocumentVerificationTests
{
    [Fact]
    public void A_rejected_document_blocks_sending_and_the_error_names_it()
    {
        var request = DraftWithSignatureOnPage(1);
        var document = request.Documents.Single();
        request.RejectDocument(
            document.Id,
            DocumentRejection.Create("Signature.DocumentValidation.AlreadySigned", "PDF already contains signatures.").Value
        );

        var result = request.Send(DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.DocumentRejected", result.Error.Code);
        Assert.Contains("Engagement letter", result.Error.Message);
        Assert.Contains("PDF already contains signatures.", result.Error.Message);
        Assert.Equal(DocumentVerificationStatus.Rejected, document.VerificationStatus);
        Assert.Null(document.DocumentHashPre);
    }

    [Fact]
    public void The_page_count_that_counts_is_the_one_the_server_measured()
    {
        var request = Draft(clientPageCount: 3);
        var document = request.Documents.Single();

        request.AttachVerifiedDocument(document.Id, VerifiedDocument.Create(Hash('a'), 7).Value);

        Assert.Equal(7, document.PageCount);
        Assert.Equal(DocumentVerificationStatus.Verified, document.VerificationStatus);
    }

    [Fact]
    public void Replacing_a_rejected_file_puts_the_document_back_to_pending()
    {
        var request = Draft(clientPageCount: null);
        var document = request.Documents.Single();
        request.RejectDocument(document.Id, DocumentRejection.Create("Signature.DocumentValidation.MimeType", "Only PDF.").Value);

        request.ReplaceDocumentFile(document.Id, Guid.NewGuid(), null);

        Assert.Equal(DocumentVerificationStatus.Pending, document.VerificationStatus);
        Assert.Null(document.RejectionCode);
        Assert.Null(document.RejectionReason);
    }

    [Fact]
    public void A_field_on_a_page_the_verified_pdf_does_not_have_blocks_sending()
    {
        var request = DraftWithSignatureOnPage(4);
        request.AttachVerifiedDocument(request.Documents.Single().Id, VerifiedDocument.Create(Hash('a'), 2).Value);

        var result = request.Send(DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.FieldOutsideDocument", result.Error.Code);
    }

    [Fact]
    public void A_verified_document_with_fields_inside_its_pages_can_be_sent()
    {
        var request = DraftWithSignatureOnPage(2);
        request.AttachVerifiedDocument(request.Documents.Single().Id, VerifiedDocument.Create(Hash('a'), 2).Value);

        Assert.True(request.Send(DateTime.UtcNow).IsSuccess);
    }

    [Fact]
    public void Documents_can_only_be_verified_while_the_request_is_a_draft()
    {
        var request = DraftWithSignatureOnPage(1);
        var document = request.Documents.Single();
        request.AttachVerifiedDocument(document.Id, VerifiedDocument.Create(Hash('a'), 1).Value);
        request.Send(DateTime.UtcNow);

        var result = request.RejectDocument(document.Id, DocumentRejection.Create("X", "Late.").Value);

        Assert.True(result.IsFailure);
        Assert.Equal(DocumentVerificationStatus.Verified, document.VerificationStatus);
    }

    private static SignatureRequest DraftWithSignatureOnPage(int page)
    {
        var request = Draft(clientPageCount: null);
        var signer = request
            .AddSigner(TestSignatureLimits.Default, SignerEmail.Create("client@example.com").Value, SignerFullName.Create("Client One").Value, null)
            .Value;
        request.PlaceField(
            TestSignatureLimits.MaxFieldsPerSigner,
            signer.Id,
            request.Documents.Single().Id,
            SignatureFieldKind.Signature,
            FieldPosition.Create(page, 0.1, 0.1, 0.2, 0.05).Value,
            null,
            true
        );
        return request;
    }

    private static SignatureRequest Draft(int? clientPageCount)
    {
        var request = SignatureRequest
            .CreateDraft(TestSignatureLimits.Default, Guid.NewGuid(), Guid.NewGuid(), "Engagement letter", null, "Fiscal", 72, false, false, true)
            .Value;
        request.AddDocument(TestSignatureLimits.Default, Guid.NewGuid(), "Engagement letter", null, clientPageCount);
        return request;
    }

    private static DocumentHash Hash(char c) => DocumentHash.Create(new string(c, 64)).Value;
}
