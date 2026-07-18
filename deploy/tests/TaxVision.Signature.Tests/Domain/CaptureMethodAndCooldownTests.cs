using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Tests.Domain;

public sealed class CaptureMethodAndCooldownTests
{
    // -------------------- SignatureCaptureMethod evidence --------------------

    [Fact]
    public void MarkSignerSigned_typed_defaults_to_signer_fullname_when_omitted()
    {
        // El aggregate acepta typedName null en Typed — usa FullName del signer como
        // fallback. La validación estricta (typed debe coincidir con FullName) vive en el
        // SubmitSignatureHandler para el flujo público.
        var request = NewInProgress();
        var signer = request.Signers.Single();

        var result = request.MarkSignerSigned(
            signer.Id,
            DateTime.UtcNow,
            SignatureCaptureMethod.Typed,
            typedName: null,
            signatureImageFileId: null,
            clientIp: null,
            userAgent: null
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(signer.FullName.Value, signer.TypedName);
    }

    [Fact]
    public void MarkSignerSigned_drawn_requires_file_id()
    {
        var request = NewInProgress();
        var signer = request.Signers.Single();

        var result = request.MarkSignerSigned(
            signer.Id,
            DateTime.UtcNow,
            SignatureCaptureMethod.Drawn,
            typedName: null,
            signatureImageFileId: null,
            clientIp: null,
            userAgent: null
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Signer.SignatureImageRequired", result.Error.Code);
    }

    [Fact]
    public void MarkSignerSigned_uploaded_requires_file_id()
    {
        var request = NewInProgress();
        var signer = request.Signers.Single();

        var result = request.MarkSignerSigned(
            signer.Id,
            DateTime.UtcNow,
            SignatureCaptureMethod.Uploaded,
            typedName: null,
            signatureImageFileId: Guid.Empty,
            clientIp: null,
            userAgent: null
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Signer.SignatureImageRequired", result.Error.Code);
    }

    [Fact]
    public void MarkSignerSigned_drawn_succeeds_and_persists_file_id()
    {
        var request = NewInProgress();
        var signer = request.Signers.Single();
        var fileId = Guid.NewGuid();

        var result = request.MarkSignerSigned(
            signer.Id,
            DateTime.UtcNow,
            SignatureCaptureMethod.Drawn,
            typedName: null,
            signatureImageFileId: fileId,
            clientIp: "1.2.3.4",
            userAgent: "ua/1.0"
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(SignatureCaptureMethod.Drawn, signer.CaptureMethod);
        Assert.Equal(fileId, signer.SignatureImageFileId);
        Assert.Null(signer.TypedName);
    }

    [Fact]
    public void MarkSignerSigned_typed_persists_typed_name_and_drops_image()
    {
        var request = NewInProgress();
        var signer = request.Signers.Single();

        var result = request.MarkSignerSigned(
            signer.Id,
            DateTime.UtcNow,
            SignatureCaptureMethod.Typed,
            typedName: "The Signer",
            signatureImageFileId: Guid.NewGuid(),
            clientIp: null,
            userAgent: null
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(SignatureCaptureMethod.Typed, signer.CaptureMethod);
        Assert.Equal("The Signer", signer.TypedName);
        Assert.Null(signer.SignatureImageFileId);
    }

    // -------------------- Challenge cooldown / switch-channel --------------------

    [Fact]
    public void IssueVerificationChallenge_within_cooldown_fails()
    {
        var request = NewInProgressWithPhone();
        var signer = request.Signers.Single();
        var now = DateTime.UtcNow;
        request.IssueVerificationChallenge(
            signer.Id,
            SignerVerificationMethod.SmsOtp,
            "hash-1",
            now,
            TimeSpan.FromMinutes(10)
        );

        var again = request.IssueVerificationChallenge(
            signer.Id,
            SignerVerificationMethod.SmsOtp,
            "hash-2",
            now.AddSeconds(10),
            TimeSpan.FromMinutes(10)
        );

        Assert.True(again.IsFailure);
        Assert.Equal("Signature.Signer.ChallengeCooldown", again.Error.Code);
    }

    [Fact]
    public void IssueVerificationChallenge_switch_channel_bypasses_cooldown()
    {
        var request = NewInProgressWithPhone();
        var signer = request.Signers.Single();
        var now = DateTime.UtcNow;
        request.IssueVerificationChallenge(
            signer.Id,
            SignerVerificationMethod.SmsOtp,
            "hash-sms",
            now,
            TimeSpan.FromMinutes(10)
        );

        var email = request.IssueVerificationChallenge(
            signer.Id,
            SignerVerificationMethod.EmailOtp,
            "hash-email",
            now.AddSeconds(2),
            TimeSpan.FromMinutes(10)
        );

        Assert.True(email.IsSuccess);
    }

    [Fact]
    public void IssueVerificationChallenge_after_cooldown_succeeds()
    {
        var request = NewInProgressWithPhone();
        var signer = request.Signers.Single();
        var now = DateTime.UtcNow;
        request.IssueVerificationChallenge(
            signer.Id,
            SignerVerificationMethod.SmsOtp,
            "hash-old",
            now,
            TimeSpan.FromMinutes(10)
        );

        var afterCooldown = request.IssueVerificationChallenge(
            signer.Id,
            SignerVerificationMethod.SmsOtp,
            "hash-new",
            now.Add(SignatureRequest.ChallengeResendCooldown).AddSeconds(1),
            TimeSpan.FromMinutes(10)
        );

        Assert.True(afterCooldown.IsSuccess);
    }

    // ================== helpers ==================

    private static SignatureRequest NewInProgress()
    {
        var draft = SignatureRequest
            .CreateDraft(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Test",
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
            .AddSigner(SignerEmail.Create("s@example.com").Value, SignerFullName.Create("The Signer").Value, null)
            .Value;
        var pos = FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value;
        draft.PlaceField(signer.Id, SignatureFieldKind.Signature, pos, null, false);
        draft.MarkReadyForSending(DocumentHash.Create(new string('a', 64)).Value);
        draft.Send(DateTime.UtcNow);
        return draft;
    }

    private static SignatureRequest NewInProgressWithPhone()
    {
        var draft = SignatureRequest
            .CreateDraft(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Test",
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
            .AddSigner(
                SignerEmail.Create("s@example.com").Value,
                SignerFullName.Create("The Signer").Value,
                null,
                SignerPhoneNumber.Create("+17865550123").Value
            )
            .Value;
        var pos = FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value;
        draft.PlaceField(signer.Id, SignatureFieldKind.Signature, pos, null, false);
        draft.MarkReadyForSending(DocumentHash.Create(new string('a', 64)).Value);
        draft.Send(DateTime.UtcNow);
        return draft;
    }
}
