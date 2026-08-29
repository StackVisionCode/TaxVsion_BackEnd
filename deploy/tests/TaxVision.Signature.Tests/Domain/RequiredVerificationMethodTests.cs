using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Tests.Domain;

/// <summary>
/// Gate de verificación de identidad por firmante (OTP). Espejo del gate del PIN pero a
/// nivel de cada <see cref="Signer"/>: si tiene <c>RequiredVerificationMethod</c> y no
/// completó el challenge, <c>MarkSignerSigned</c> lo rechaza. Lo que expone el
/// <c>PublicSignerView</c> (<c>RequiredVerificationMethod</c> + <c>IsVerificationCompleted</c>)
/// se deriva directo de <see cref="Signer.HasCompletedVerification"/>.
/// </summary>
public sealed class RequiredVerificationMethodTests
{
    [Fact]
    public void MarkSignerSigned_blocked_when_verification_required_and_not_completed()
    {
        var request = NewInProgressRequiring(SignerVerificationMethod.EmailOtp);
        var signer = request.Signers.Single();

        var result = request.MarkSignerSigned(signer.Id, DateTime.UtcNow, clientIp: null, userAgent: null);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.VerificationRequired", result.Error.Code);
    }

    [Fact]
    public void MarkSignerSigned_succeeds_after_challenge_consumed()
    {
        var request = NewInProgressRequiring(SignerVerificationMethod.EmailOtp);
        var signer = request.Signers.Single();
        var now = DateTime.UtcNow;

        request.IssueVerificationChallenge(
            signer.Id,
            SignerVerificationMethod.EmailOtp,
            "hash",
            now,
            TimeSpan.FromMinutes(10)
        );
        var verify = request.VerifyVerificationChallenge(
            signer.Id,
            SignerVerificationMethod.EmailOtp,
            isMatch: true,
            now.AddSeconds(5),
            clientIp: null,
            userAgent: null
        );
        Assert.True(verify.IsSuccess);
        Assert.True(signer.HasCompletedVerification(SignerVerificationMethod.EmailOtp));

        var result = request.MarkSignerSigned(signer.Id, now.AddSeconds(10), clientIp: null, userAgent: null);

        Assert.True(result.IsSuccess);
        Assert.Equal(SignerStatus.Signed, signer.Status);
    }

    [Fact]
    public void MarkSignerSigned_unaffected_when_no_method_required()
    {
        var request = NewInProgressRequiring(null);
        var signer = request.Signers.Single();

        var result = request.MarkSignerSigned(signer.Id, DateTime.UtcNow, clientIp: null, userAgent: null);

        Assert.True(result.IsSuccess);
        Assert.Null(signer.RequiredVerificationMethod);
    }

    [Fact]
    public void AddSigner_rejects_practitioner_pin_as_verification_method()
    {
        var draft = NewDraft();

        var result = draft.AddSigner(
            SignerEmail.Create("s@example.com").Value,
            SignerFullName.Create("The Signer").Value,
            null,
            phoneNumber: null,
            language: null,
            requiredVerificationMethod: SignerVerificationMethod.PractitionerPin
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Signer.VerificationMethod", result.Error.Code);
    }

    [Fact]
    public void SetSignerRequiredVerificationMethod_sets_and_clears_in_draft()
    {
        var draft = NewDraft();
        var signer = draft
            .AddSigner(SignerEmail.Create("s@example.com").Value, SignerFullName.Create("The Signer").Value, null)
            .Value;

        Assert.True(draft.SetSignerRequiredVerificationMethod(signer.Id, SignerVerificationMethod.SmsOtp).IsSuccess);
        Assert.Equal(SignerVerificationMethod.SmsOtp, signer.RequiredVerificationMethod);

        Assert.True(draft.SetSignerRequiredVerificationMethod(signer.Id, null).IsSuccess);
        Assert.Null(signer.RequiredVerificationMethod);
    }

    [Fact]
    public void SetSignerRequiredVerificationMethod_rejects_practitioner_pin()
    {
        var draft = NewDraft();
        var signer = draft
            .AddSigner(SignerEmail.Create("s@example.com").Value, SignerFullName.Create("The Signer").Value, null)
            .Value;

        var result = draft.SetSignerRequiredVerificationMethod(signer.Id, SignerVerificationMethod.PractitionerPin);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Signer.VerificationMethod", result.Error.Code);
    }

    // ================== helpers ==================

    private static SignatureRequest NewDraft() =>
        SignatureRequest
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

    private static SignatureRequest NewInProgressRequiring(SignerVerificationMethod? method)
    {
        var draft = NewDraft();
        var signer = draft
            .AddSigner(
                SignerEmail.Create("s@example.com").Value,
                SignerFullName.Create("The Signer").Value,
                null,
                phoneNumber: null,
                language: null,
                requiredVerificationMethod: method
            )
            .Value;
        var pos = FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value;
        draft.PlaceField(signer.Id, SignatureFieldKind.Signature, pos, null, false);
        draft.MarkReadyForSending(DocumentHash.Create(new string('a', 64)).Value);
        draft.Send(DateTime.UtcNow);
        return draft;
    }
}
