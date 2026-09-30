using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Tests.Domain;

public sealed class PreparerAndFrameworkTests
{
    // -------------------- PreparerInfo VO --------------------

    [Fact]
    public void PreparerInfo_normalizes_identifier_to_uppercase()
    {
        var result = PreparerInfo.Create("p12345678", "Jane Doe, EA", "Enrolled Agent");

        Assert.True(result.IsSuccess);
        Assert.Equal("P12345678", result.Value.PtinOrEfin);
        Assert.Equal("Jane Doe, EA", result.Value.DisplayName);
        Assert.Equal("Enrolled Agent", result.Value.TitleLabel);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("has spaces here")]
    [InlineData("!@#not-alnum")]
    public void PreparerInfo_rejects_invalid_identifier(string raw)
    {
        Assert.True(PreparerInfo.Create(raw, "Jane Doe", null).IsFailure);
    }

    // -------------------- SetPreparer / SignAsPreparer --------------------

    [Fact]
    public void SetPreparer_only_in_draft_or_ready()
    {
        var draft = NewDraft();
        var preparer = PreparerInfo.Create("P12345678", "Jane Doe, EA", null).Value;

        Assert.True(draft.SetPreparer(preparer).IsSuccess);
        Assert.NotNull(draft.Preparer);
    }

    [Fact]
    public void MarkPreparerSigned_requires_preparer_assigned()
    {
        var request = NewInProgressWithField();

        var result = request.MarkPreparerSigned(Guid.NewGuid(), DateTime.UtcNow, null, null);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.NoPreparer", result.Error.Code);
    }

    [Fact]
    public void MarkPreparerSigned_succeeds_after_assignment_and_send()
    {
        var request = NewInProgressWithFieldAndPreparer();
        var userId = Guid.NewGuid();

        var result = request.MarkPreparerSigned(userId, DateTime.UtcNow, null, null);

        Assert.True(result.IsSuccess);
        Assert.True(request.IsPreparerSigned);
        Assert.Equal(userId, request.PreparerSignedByUserId);
    }

    [Fact]
    public void MarkPreparerSigned_is_idempotent_for_same_user()
    {
        var request = NewInProgressWithFieldAndPreparer();
        var userId = Guid.NewGuid();
        request.MarkPreparerSigned(userId, DateTime.UtcNow, null, null);

        var second = request.MarkPreparerSigned(userId, DateTime.UtcNow.AddMinutes(1), null, null);

        Assert.True(second.IsSuccess);
    }

    [Fact]
    public void MarkPreparerSigned_rejects_different_user_after_signed()
    {
        var request = NewInProgressWithFieldAndPreparer();
        request.MarkPreparerSigned(Guid.NewGuid(), DateTime.UtcNow, null, null);

        var second = request.MarkPreparerSigned(Guid.NewGuid(), DateTime.UtcNow, null, null);

        Assert.True(second.IsFailure);
        Assert.Equal("Signature.Request.PreparerAlreadySigned", second.Error.Code);
    }

    // -------------------- A1: el preparer ligado a un usuario --------------------

    /// <summary>
    /// El PTIN/EFIN identifica a un profesional concreto ante el IRS (Pub. 1345, §6109(a)(4)). Antes de
    /// A1 era un dato suelto en el cuerpo del request: cualquier empleado con
    /// <c>signature.document.sign</c> firmaba como preparer y el PDF sellado salía con la credencial del
    /// colega que estuviera declarada. Eso no es un problema de permisos, es suplantación.
    /// </summary>
    [Fact]
    public void MarkPreparerSigned_rejects_a_user_who_is_not_the_assigned_preparer()
    {
        var preparerUserId = Guid.NewGuid();
        var request = NewInProgressWithPreparerBoundTo(preparerUserId);

        var result = request.MarkPreparerSigned(Guid.NewGuid(), DateTime.UtcNow, null, null);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.PreparerNotSelf", result.Error.Code);
        Assert.False(request.IsPreparerSigned);
    }

    [Fact]
    public void MarkPreparerSigned_accepts_the_assigned_preparer()
    {
        var preparerUserId = Guid.NewGuid();
        var request = NewInProgressWithPreparerBoundTo(preparerUserId);

        var result = request.MarkPreparerSigned(preparerUserId, DateTime.UtcNow, null, null);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(preparerUserId, request.PreparerSignedByUserId);
    }

    /// <summary>
    /// §R.7 — las solicitudes creadas antes de A1 no tienen el preparer ligado a nadie, y su
    /// comportamiento no puede cambiar: si el chequeo las bloqueara, una firma en curso quedaría
    /// imposible de completar.
    /// </summary>
    [Fact]
    public void A_request_from_before_the_binding_keeps_working()
    {
        var request = NewInProgressWithFieldAndPreparer();
        Assert.Null(request.Preparer!.UserId);

        Assert.True(request.MarkPreparerSigned(Guid.NewGuid(), DateTime.UtcNow, null, null).IsSuccess);
    }

    [Fact]
    public void PreparerInfo_keeps_the_user_it_was_created_with()
    {
        var userId = Guid.NewGuid();

        var preparer = PreparerInfo.Create("P12345678", "Jane Doe, EA", null, userId);

        Assert.True(preparer.IsSuccess);
        Assert.Equal(userId, preparer.Value.UserId);
    }

    [Fact]
    public void PreparerInfo_rejects_an_empty_user()
    {
        var result = PreparerInfo.Create("P12345678", "Jane Doe, EA", null, Guid.Empty);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Preparer.User", result.Error.Code);
    }

    private static SignatureRequest NewInProgressWithPreparerBoundTo(Guid preparerUserId)
    {
        var draft = NewDraft();
        var signer = draft
            .AddSigner(SignerEmail.Create("s@example.com").Value, SignerFullName.Create("Signer One").Value, null)
            .Value;
        var pos = FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value;
        draft.PlaceField(signer.Id, SignatureFieldKind.Signature, pos, null, false);
        draft.SetPreparer(PreparerInfo.Create("P12345678", "Jane Doe, EA", "Enrolled Agent", preparerUserId).Value);
        draft.MarkReadyForSending(DocumentHash.Create(new string('a', 64)).Value);
        draft.Send(DateTime.UtcNow);
        return draft;
    }

    // -------------------- SignerPhoneNumber VO --------------------

    [Fact]
    public void SignerPhoneNumber_normalizes_and_accepts_e164()
    {
        var result = SignerPhoneNumber.Create("+1 (786) 555-0123");

        Assert.True(result.IsSuccess);
        Assert.Equal("+17865550123", result.Value.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("786-555-0123")]
    [InlineData("+123")]
    public void SignerPhoneNumber_rejects_invalid(string raw)
    {
        Assert.True(SignerPhoneNumber.Create(raw).IsFailure);
    }

    // -------------------- Verification framework --------------------

    [Fact]
    public void IssueVerificationChallenge_rejects_pin_method()
    {
        var request = NewInProgressWithField();
        var signer = request.Signers.Single();

        var result = request.IssueVerificationChallenge(
            signer.Id,
            SignerVerificationMethod.PractitionerPin,
            "hash",
            DateTime.UtcNow,
            TimeSpan.FromMinutes(10)
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.PinNotChallenge", result.Error.Code);
    }

    [Fact]
    public void IssueVerificationChallenge_sms_requires_phone_number()
    {
        var request = NewInProgressWithField();
        var signer = request.Signers.Single();

        var result = request.IssueVerificationChallenge(
            signer.Id,
            SignerVerificationMethod.SmsOtp,
            "hash",
            DateTime.UtcNow,
            TimeSpan.FromMinutes(10)
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Signer.PhoneMissing", result.Error.Code);
    }

    [Fact]
    public void IssueVerificationChallenge_sms_succeeds_when_phone_present()
    {
        var request = NewInProgressWithFieldAndPhone();
        var signer = request.Signers.Single();

        var result = request.IssueVerificationChallenge(
            signer.Id,
            SignerVerificationMethod.SmsOtp,
            "hash-123",
            DateTime.UtcNow,
            TimeSpan.FromMinutes(10)
        );

        Assert.True(result.IsSuccess);
        var current = signer.CurrentChallengeFor(SignerVerificationMethod.SmsOtp, DateTime.UtcNow);
        Assert.NotNull(current);
        Assert.Equal("hash-123", current!.AnswerHash);
    }

    [Fact]
    public void IssueVerificationChallenge_invalidates_previous_active()
    {
        var request = NewInProgressWithFieldAndPhone();
        var signer = request.Signers.Single();
        var now = DateTime.UtcNow;
        request.IssueVerificationChallenge(
            signer.Id,
            SignerVerificationMethod.SmsOtp,
            "hash-old",
            now,
            TimeSpan.FromMinutes(10)
        );

        request.IssueVerificationChallenge(
            signer.Id,
            SignerVerificationMethod.SmsOtp,
            "hash-new",
            now.AddSeconds(30),
            TimeSpan.FromMinutes(10)
        );

        var activeCount = signer.Challenges.Count(c => c.IsActiveAt(now.AddMinutes(1)));
        Assert.Equal(1, activeCount);
    }

    [Fact]
    public void VerifyVerificationChallenge_succeeds_and_flags_signer_verified()
    {
        var request = NewInProgressWithFieldAndPhone();
        var signer = request.Signers.Single();
        var issued = request
            .IssueVerificationChallenge(
                signer.Id,
                SignerVerificationMethod.SmsOtp,
                "hash-x",
                DateTime.UtcNow,
                TimeSpan.FromMinutes(10)
            )
            .Value;

        var result = request.VerifyVerificationChallenge(
            signer.Id,
            SignerVerificationMethod.SmsOtp,
            isMatch: true,
            DateTime.UtcNow,
            "1.2.3.4",
            "ua/1.0"
        );

        Assert.True(result.IsSuccess);
        Assert.True(signer.IsPinVerified); // generic verified flag (semantic naming inherited)
        Assert.True(issued.IsConsumed);
    }

    [Fact]
    public void VerifyVerificationChallenge_no_active_returns_error()
    {
        var request = NewInProgressWithFieldAndPhone();
        var signer = request.Signers.Single();

        var result = request.VerifyVerificationChallenge(
            signer.Id,
            SignerVerificationMethod.SmsOtp,
            isMatch: true,
            DateTime.UtcNow,
            null,
            null
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Signer.NoActiveChallenge", result.Error.Code);
    }

    [Fact]
    public void VerifyVerificationChallenge_pin_method_rejected()
    {
        var request = NewInProgressWithField();
        var signer = request.Signers.Single();

        var result = request.VerifyVerificationChallenge(
            signer.Id,
            SignerVerificationMethod.PractitionerPin,
            isMatch: true,
            DateTime.UtcNow,
            null,
            null
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.PinNotChallenge", result.Error.Code);
    }

    // ================== helpers ==================

    private static SignatureRequest NewDraft() =>
        SignatureRequest
            .CreateDraft(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Test Preparer/Framework",
                null,
                "Fiscal",
                Guid.NewGuid(),
                tokenExpirationHours: 72,
                requiresSequentialSigning: false,
                requiresConsent: false,
                generateCertificate: false
            )
            .Value;

    private static SignatureRequest NewInProgressWithField()
    {
        var draft = NewDraft();
        var signer = draft
            .AddSigner(SignerEmail.Create("s@example.com").Value, SignerFullName.Create("Signer One").Value, null)
            .Value;
        var pos = FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value;
        draft.PlaceField(signer.Id, SignatureFieldKind.Signature, pos, null, false);
        draft.MarkReadyForSending(DocumentHash.Create(new string('a', 64)).Value);
        draft.Send(DateTime.UtcNow);
        return draft;
    }

    private static SignatureRequest NewInProgressWithFieldAndPhone()
    {
        var draft = NewDraft();
        var signer = draft
            .AddSigner(
                SignerEmail.Create("s@example.com").Value,
                SignerFullName.Create("Signer One").Value,
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

    private static SignatureRequest NewInProgressWithFieldAndPreparer()
    {
        var draft = NewDraft();
        var signer = draft
            .AddSigner(SignerEmail.Create("s@example.com").Value, SignerFullName.Create("Signer One").Value, null)
            .Value;
        var pos = FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value;
        draft.PlaceField(signer.Id, SignatureFieldKind.Signature, pos, null, false);
        draft.SetPreparer(PreparerInfo.Create("P12345678", "Jane Doe, EA", "Enrolled Agent").Value);
        draft.MarkReadyForSending(DocumentHash.Create(new string('a', 64)).Value);
        draft.Send(DateTime.UtcNow);
        return draft;
    }
}
