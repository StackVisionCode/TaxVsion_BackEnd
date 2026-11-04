using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Tests.Application.Offboarding;

namespace TaxVision.Signature.Tests.Domain;

/// <summary>
/// Cambio de preparador en solicitudes enviadas, autoría de borradores y firma congelada del preparador
/// (D-2 del retiro de empleados).
/// </summary>
public sealed class SignatureRequestPreparerChangeTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void A_sent_request_changes_preparer_and_restamps_the_whole_identity()
    {
        var leaver = Guid.NewGuid();
        var successor = Guid.NewGuid();
        var request = PreparerRequests.Sent(Tenant, Guid.NewGuid(), leaver, signatureFileId: Guid.NewGuid());
        var newSignature = Guid.NewGuid();

        var result = request.ChangePreparer(
            PreparerRequests.Identity(successor, "P99887766", "John Roe, CPA"),
            newSignature
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(successor, request.Preparer!.UserId);
        Assert.Equal("P99887766", request.Preparer.PtinOrEfin);
        Assert.Equal("John Roe, CPA", request.Preparer.DisplayName);
        Assert.Equal(newSignature, request.PreparerSignatureFileId);
        Assert.Equal(SignatureRequestStatus.InProgress, request.Status);
    }

    [Fact]
    public void Without_a_new_signature_the_old_preparer_image_is_not_kept()
    {
        var request = PreparerRequests.Sent(Tenant, Guid.NewGuid(), Guid.NewGuid(), signatureFileId: Guid.NewGuid());

        request.ChangePreparer(PreparerRequests.Identity(Guid.NewGuid()), signatureFileId: null);

        Assert.Null(request.PreparerSignatureFileId);
    }

    [Fact]
    public void After_the_preparer_signed_the_preparer_can_no_longer_change()
    {
        var preparer = Guid.NewGuid();
        var request = PreparerRequests.Sent(Tenant, Guid.NewGuid(), preparer);
        request.MarkPreparerSigned(preparer, DateTime.UtcNow, null, null);

        var result = request.ChangePreparer(PreparerRequests.Identity(Guid.NewGuid()), null);

        Assert.Equal("Signature.Request.PreparerAlreadySigned", result.Error.Code);
        Assert.Equal(preparer, request.Preparer!.UserId);
    }

    [Fact]
    public void Once_a_document_is_sealed_the_preparer_can_no_longer_change()
    {
        // Dos documentos con un firmante cada uno: el primero se sella mientras la solicitud sigue en curso.
        var request = PreparerRequests.DraftWithPreparer(Tenant, Guid.NewGuid(), Guid.NewGuid());
        request.AddDocument(TestSignatureLimits.Default, Guid.NewGuid(), "Engagement letter");
        var signers = new List<Signer>();
        foreach (var (document, index) in request.Documents.Select((document, index) => (document, index)))
        {
            var signer = request
                .AddSigner(
                    TestSignatureLimits.Default,
                    SignerEmail.Create($"c{index}@example.com").Value,
                    SignerFullName.Create($"Client {index} Doe").Value,
                    null
                )
                .Value;
            request.PlaceField(
                TestSignatureLimits.MaxFieldsPerSigner,
                signer.Id,
                document.Id,
                SignatureFieldKind.Signature,
                FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value,
                null,
                false
            );
            request.AttachVerifiedDocument(document.Id, VerifiedDocument.Create(PreparerRequests.Hash('a'), 50).Value);
            signers.Add(signer);
        }
        request.Send(DateTime.UtcNow);
        var first = request.Documents.OrderBy(document => document.Order).First();
        request.MarkSignerDocumentsCompleted(
            signers[0].Id,
            [first.Id],
            DateTime.UtcNow,
            SignatureCaptureMethod.Typed,
            "Client 0 Doe",
            null,
            null,
            null
        );
        Assert.True(
            request.MarkDocumentSealed(first.Id, Guid.NewGuid(), PreparerRequests.Hash('b'), DateTime.UtcNow).IsSuccess
        );
        Assert.Equal(SignatureRequestStatus.InProgress, request.Status);

        var result = request.CanChangePreparer(Guid.NewGuid());

        Assert.Equal("Signature.Request.PreparerStampApplied", result.Error.Code);
    }

    [Fact]
    public void A_draft_keeps_using_set_preparer_instead()
    {
        var request = PreparerRequests.DraftWithPreparer(Tenant, Guid.NewGuid(), Guid.NewGuid());

        var result = request.ChangePreparer(PreparerRequests.Identity(Guid.NewGuid()), null);

        Assert.Equal("Signature.Request.PreparerChangeStatus", result.Error.Code);
    }

    [Fact]
    public void A_sent_request_without_preparer_cannot_get_one_this_way()
    {
        var request = PreparerRequests.Sent(Tenant, Guid.NewGuid(), preparerId: null);

        var result = request.ChangePreparer(PreparerRequests.Identity(Guid.NewGuid()), null);

        Assert.Equal("Signature.Request.NoPreparer", result.Error.Code);
        Assert.Null(request.Preparer);
    }

    [Fact]
    public void Changing_to_the_same_preparer_is_a_no_op()
    {
        var preparer = Guid.NewGuid();
        var request = PreparerRequests.Sent(Tenant, Guid.NewGuid(), preparer, signatureFileId: Guid.NewGuid());
        var before = request.PreparerSignatureFileId;

        var result = request.ChangePreparer(PreparerRequests.Identity(preparer, "P00000001"), Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.Equal("P12345678", request.Preparer!.PtinOrEfin);
        Assert.Equal(before, request.PreparerSignatureFileId);
    }

    [Fact]
    public void An_unsent_request_changes_author()
    {
        var successor = Guid.NewGuid();
        var draft = PreparerRequests.Draft(Tenant, Guid.NewGuid());

        Assert.True(draft.ReassignAuthor(successor).IsSuccess);
        Assert.Equal(successor, draft.CreatedByUserId);
    }

    [Fact]
    public void A_sent_request_keeps_its_author()
    {
        var author = Guid.NewGuid();
        var request = PreparerRequests.Sent(Tenant, author, Guid.NewGuid());

        var result = request.ReassignAuthor(Guid.NewGuid());

        Assert.Equal("Signature.Request.AuthorLocked", result.Error.Code);
        Assert.Equal(author, request.CreatedByUserId);
    }

    [Fact]
    public void A_draft_drops_its_frozen_preparer_signature()
    {
        var draft = PreparerRequests.DraftWithPreparer(Tenant, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        Assert.True(draft.ClearPreparerSignature().IsSuccess);
        Assert.Null(draft.PreparerSignatureFileId);
    }
}
