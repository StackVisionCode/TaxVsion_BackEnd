using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Tests.Domain;

public sealed class PractitionerPinTests
{
    // -------------------- VO --------------------

    [Fact]
    public void PractitionerPin_accepts_valid_numeric_pin()
    {
        var result = PractitionerPin.Create("1357");

        Assert.True(result.IsSuccess);
        Assert.Equal("1357", result.Value.Value);
    }

    [Fact]
    public void PractitionerPin_trims_whitespace()
    {
        var result = PractitionerPin.Create("  9876  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("9876", result.Value.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("12345678901")]
    [InlineData("12a4")]
    [InlineData("1 234")]
    public void PractitionerPin_rejects_invalid_values(string raw)
    {
        Assert.True(PractitionerPin.Create(raw).IsFailure);
    }

    [Fact]
    public void PractitionerPin_ToString_masks_value_for_log_safety()
    {
        var pin = PractitionerPin.Create("1234").Value;
        Assert.Equal("***", pin.ToString());
    }

    // -------------------- Aggregate: SetPractitionerPin --------------------

    [Fact]
    public void SetPractitionerPin_stores_hash_and_flips_requirement()
    {
        var request = NewDraft();
        var userId = Guid.NewGuid();

        var result = request.SetPractitionerPin("hash-value", userId, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal("hash-value", request.PractitionerPinHash);
        Assert.True(request.RequiresPractitionerPin);
        Assert.Equal(userId, request.PractitionerPinSetByUserId);
    }

    [Fact]
    public void SetPractitionerPin_fails_on_empty_hash()
    {
        var request = NewDraft();

        var result = request.SetPractitionerPin("", Guid.NewGuid(), DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.PinHash", result.Error.Code);
    }

    [Fact]
    public void SetPractitionerPin_after_send_throws()
    {
        var request = NewInProgressWithField();

        Assert.Throws<InvalidOperationException>(() =>
            request.SetPractitionerPin("hash", Guid.NewGuid(), DateTime.UtcNow)
        );
    }

    [Fact]
    public void ClearPractitionerPin_removes_requirement()
    {
        var request = NewDraft();
        request.SetPractitionerPin("hash", Guid.NewGuid(), DateTime.UtcNow);

        var result = request.ClearPractitionerPin();

        Assert.True(result.IsSuccess);
        Assert.False(request.RequiresPractitionerPin);
    }

    // -------------------- VerifySignerWithPin --------------------

    [Fact]
    public void VerifySignerWithPin_fails_when_pin_not_configured()
    {
        var request = NewInProgressWithField();
        var signer = request.Signers.Single();

        var result = request.VerifySignerWithPin(signer.Id, isMatch: true, DateTime.UtcNow, null, null);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.PinNotConfigured", result.Error.Code);
    }

    [Fact]
    public void VerifySignerWithPin_success_marks_signer_verified()
    {
        var request = NewInProgressWithPin();
        var signer = request.Signers.Single();

        var result = request.VerifySignerWithPin(signer.Id, isMatch: true, DateTime.UtcNow, "1.2.3.4", "ua/1.0");

        Assert.True(result.IsSuccess);
        Assert.True(signer.IsPinVerified);
        Assert.Equal(0, signer.PinFailedAttempts);
    }

    [Fact]
    public void VerifySignerWithPin_failure_increments_counter()
    {
        var request = NewInProgressWithPin();
        var signer = request.Signers.Single();

        request.VerifySignerWithPin(signer.Id, isMatch: false, DateTime.UtcNow, null, null);
        request.VerifySignerWithPin(signer.Id, isMatch: false, DateTime.UtcNow, null, null);

        Assert.Equal(2, signer.PinFailedAttempts);
        Assert.False(signer.IsPinVerified);
        Assert.Null(signer.PinLockedUntilUtc);
    }

    [Fact]
    public void VerifySignerWithPin_locks_after_max_attempts()
    {
        var request = NewInProgressWithPin();
        var signer = request.Signers.Single();
        var now = DateTime.UtcNow;

        for (var i = 0; i < Signer.MaxPinAttempts; i++)
            request.VerifySignerWithPin(signer.Id, isMatch: false, now, null, null);

        Assert.NotNull(signer.PinLockedUntilUtc);
        Assert.Equal(Signer.MaxPinAttempts, signer.PinFailedAttempts);
        Assert.True(signer.IsPinLockedAt(now.AddMinutes(1)));
    }

    [Fact]
    public void VerifySignerWithPin_fails_while_locked()
    {
        var request = NewInProgressWithPin();
        var signer = request.Signers.Single();
        var now = DateTime.UtcNow;
        for (var i = 0; i < Signer.MaxPinAttempts; i++)
            request.VerifySignerWithPin(signer.Id, isMatch: false, now, null, null);

        var duringLock = request.VerifySignerWithPin(signer.Id, isMatch: true, now.AddMinutes(1), null, null);

        Assert.True(duringLock.IsFailure);
        Assert.Equal("Signature.Signer.PinLocked", duringLock.Error.Code);
    }

    [Fact]
    public void VerifySignerWithPin_resets_counter_after_lock_expires()
    {
        var request = NewInProgressWithPin();
        var signer = request.Signers.Single();
        var now = DateTime.UtcNow;
        for (var i = 0; i < Signer.MaxPinAttempts; i++)
            request.VerifySignerWithPin(signer.Id, isMatch: false, now, null, null);

        var afterLock = now.Add(Signer.PinLockDuration).AddMinutes(1);
        var success = request.VerifySignerWithPin(signer.Id, isMatch: true, afterLock, null, null);

        Assert.True(success.IsSuccess);
        Assert.True(signer.IsPinVerified);
    }

    [Fact]
    public void VerifySignerWithPin_is_idempotent_after_success()
    {
        var request = NewInProgressWithPin();
        var signer = request.Signers.Single();
        request.VerifySignerWithPin(signer.Id, isMatch: true, DateTime.UtcNow, null, null);
        var verifiedAt = signer.PinVerifiedAtUtc;

        var second = request.VerifySignerWithPin(signer.Id, isMatch: true, DateTime.UtcNow.AddMinutes(5), null, null);

        Assert.True(second.IsSuccess);
        Assert.Equal(verifiedAt, signer.PinVerifiedAtUtc);
    }

    // -------------------- MarkSignerSigned guard --------------------

    [Fact]
    public void MarkSignerSigned_fails_when_pin_required_and_not_verified()
    {
        var request = NewInProgressWithPin();
        var signer = request.Signers.Single();

        var result = request.MarkSignerSigned(signer.Id, DateTime.UtcNow, null, null);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.PinVerificationRequired", result.Error.Code);
    }

    [Fact]
    public void MarkSignerSigned_succeeds_after_pin_verified()
    {
        var request = NewInProgressWithPin();
        var signer = request.Signers.Single();
        request.VerifySignerWithPin(signer.Id, isMatch: true, DateTime.UtcNow, null, null);

        var result = request.MarkSignerSigned(signer.Id, DateTime.UtcNow, null, null);

        Assert.True(result.IsSuccess);
        Assert.Equal(SignatureRequestStatus.Completed, request.Status);
    }

    // ================== helpers ==================

    private static SignatureRequest NewDraft() =>
        SignatureRequest.CreateDraft(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "PIN Test",
            null,
            SignatureCategory.Fiscal,
            Guid.NewGuid(),
            tokenExpirationHours: 72,
            requiresSequentialSigning: false,
            requiresConsent: false,
            generateCertificate: false
        ).Value;

    private static SignatureRequest NewInProgressWithField()
    {
        var draft = NewDraft();
        var signer = draft.AddSigner(
            SignerEmail.Create("signer@example.com").Value,
            SignerFullName.Create("The Signer").Value,
            null
        ).Value;
        var pos = FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value;
        draft.PlaceField(signer.Id, SignatureFieldKind.Signature, pos, null, false);
        draft.MarkReadyForSending(DocumentHash.Create(new string('a', 64)).Value);
        draft.Send(DateTime.UtcNow);
        return draft;
    }

    private static SignatureRequest NewInProgressWithPin()
    {
        var draft = NewDraft();
        var signer = draft.AddSigner(
            SignerEmail.Create("signer@example.com").Value,
            SignerFullName.Create("The Signer").Value,
            null
        ).Value;
        var pos = FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value;
        draft.PlaceField(signer.Id, SignatureFieldKind.Signature, pos, null, false);
        draft.SetPractitionerPin("stored-hash", Guid.NewGuid(), DateTime.UtcNow);
        draft.MarkReadyForSending(DocumentHash.Create(new string('a', 64)).Value);
        draft.Send(DateTime.UtcNow);
        return draft;
    }
}
