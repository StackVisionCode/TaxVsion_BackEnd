using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Tests.Domain;

public sealed class MarkSealedTests
{
    // -------------------- Guard: solo desde Completed --------------------

    [Fact]
    public void MarkSealed_fails_when_not_completed()
    {
        var request = NewInProgressWithField();
        var hash = DocumentHash.Create(new string('a', 64)).Value;

        var result = request.MarkSealed(Guid.NewGuid(), hash, certificateFileId: null);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.NotCompleted", result.Error.Code);
    }

    [Fact]
    public void MarkSealed_succeeds_and_stores_sealed_file_and_hash()
    {
        var request = NewCompletedRequest();
        var hash = DocumentHash.Create(new string('b', 64)).Value;
        var sealedFileId = Guid.NewGuid();
        var certFileId = Guid.NewGuid();

        var result = request.MarkSealed(sealedFileId, hash, certFileId);

        Assert.True(result.IsSuccess);
        Assert.Equal(sealedFileId, request.SealedFileId);
        Assert.Equal(hash.Value, request.DocumentHashPost!.Value);
        Assert.Equal(certFileId, request.CertificateFileId);
    }

    [Fact]
    public void MarkSealed_certificate_is_optional()
    {
        var request = NewCompletedRequest();
        var hash = DocumentHash.Create(new string('c', 64)).Value;

        var result = request.MarkSealed(Guid.NewGuid(), hash, certificateFileId: null);

        Assert.True(result.IsSuccess);
        Assert.Null(request.CertificateFileId);
    }

    [Fact]
    public void MarkSealed_is_idempotent_for_same_ids()
    {
        var request = NewCompletedRequest();
        var hash = DocumentHash.Create(new string('d', 64)).Value;
        var sealedFileId = Guid.NewGuid();

        var first = request.MarkSealed(sealedFileId, hash, certificateFileId: null);
        var second = request.MarkSealed(sealedFileId, hash, certificateFileId: null);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
    }

    [Fact]
    public void MarkSealed_rejects_empty_sealed_file_id()
    {
        var request = NewCompletedRequest();
        var hash = DocumentHash.Create(new string('e', 64)).Value;

        var result = request.MarkSealed(Guid.Empty, hash, certificateFileId: null);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.SealedFile", result.Error.Code);
    }

    [Fact]
    public void MarkSealed_rejects_empty_certificate_id_when_provided()
    {
        var request = NewCompletedRequest();
        var hash = DocumentHash.Create(new string('f', 64)).Value;

        var result = request.MarkSealed(Guid.NewGuid(), hash, certificateFileId: Guid.Empty);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.CertificateFile", result.Error.Code);
    }

    // ================== helpers ==================

    private static SignatureRequest NewCompletedRequest()
    {
        var request = NewInProgressWithField();
        var signer = request.Signers.Single();
        request.MarkSignerSigned(signer.Id, DateTime.UtcNow, null, null);
        return request;
    }

    private static SignatureRequest NewInProgressWithField()
    {
        var draft = SignatureRequest
            .CreateDraft(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Test Sealed",
                null,
                SignatureCategory.Fiscal,
                Guid.NewGuid(),
                tokenExpirationHours: 72,
                requiresSequentialSigning: false,
                requiresConsent: false,
                generateCertificate: false
            )
            .Value;

        var signer = draft
            .AddSigner(SignerEmail.Create("s@example.com").Value, SignerFullName.Create("Some Name").Value, null)
            .Value;
        var pos = FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value;
        draft.PlaceField(signer.Id, SignatureFieldKind.Signature, pos, null, false);
        var hash = DocumentHash.Create(new string('a', 64)).Value;
        draft.MarkReadyForSending(hash);
        draft.Send(DateTime.UtcNow);
        return draft;
    }
}
