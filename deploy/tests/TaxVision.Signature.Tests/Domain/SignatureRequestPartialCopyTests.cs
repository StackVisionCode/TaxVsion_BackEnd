using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Tests.Domain;

public class SignatureRequestPartialCopyTests
{
    // ================== SetSendPartialCopy ==================

    [Fact]
    public void SetSendPartialCopy_on_without_audience_fails()
    {
        var request = NewDraft();

        var result = request.SetSendPartialCopy(enabled: true, audience: null);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.PartialCopyRequiresAudience", result.Error.Code);
    }

    [Fact]
    public void SetSendPartialCopy_specific_with_unknown_signer_fails()
    {
        var request = NewDraftWithSigner(out _);
        var audience = PartialCopyAudience.Specific([Guid.NewGuid()]).Value;

        var result = request.SetSendPartialCopy(true, audience);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.PartialCopyAudienceInvalid", result.Error.Code);
    }

    [Fact]
    public void SetSendPartialCopy_specific_with_known_signer_succeeds()
    {
        var request = NewDraftWithSigner(out var signer);
        var audience = PartialCopyAudience.Specific([signer.Id]).Value;

        var result = request.SetSendPartialCopy(true, audience);

        Assert.True(result.IsSuccess);
        Assert.True(request.SendPartialCopyOnEachSignature);
        Assert.True(request.PartialCopyAudience.Includes(signer.Id));
    }

    [Fact]
    public void SetSendPartialCopy_off_clears_audience()
    {
        var request = NewDraftWithSigner(out var signer);
        request.SetSendPartialCopy(true, PartialCopyAudience.Specific([signer.Id]).Value);

        var result = request.SetSendPartialCopy(false, null);

        Assert.True(result.IsSuccess);
        Assert.False(request.SendPartialCopyOnEachSignature);
        Assert.Equal(PartialCopyAudienceKind.All, request.PartialCopyAudience.Kind);
    }

    // ================== Expiration opcional ==================

    [Fact]
    public void DisableExpiration_clears_hours_and_expiry()
    {
        var request = NewDraft();

        var result = request.DisableExpiration();

        Assert.True(result.IsSuccess);
        Assert.False(request.ExpirationEnabled);
        Assert.Null(request.TokenExpirationHours);
        Assert.Null(request.ExpiresAtUtc);
    }

    [Fact]
    public void EnableExpiration_sets_hours_and_recomputes_from_now_when_draft()
    {
        var request = NewDraft();
        request.DisableExpiration();

        var result = request.EnableExpiration(48);

        Assert.True(result.IsSuccess);
        Assert.True(request.ExpirationEnabled);
        Assert.Equal(48, request.TokenExpirationHours);
        Assert.NotNull(request.ExpiresAtUtc);
    }

    [Fact]
    public void ExtendExpiration_fails_when_expiration_disabled()
    {
        var request = NewDraft();
        request.DisableExpiration();

        var result = request.ExtendExpiration(24);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.ExpirationDisabled", result.Error.Code);
    }

    [Fact]
    public void MarkExpired_fails_when_expiration_disabled()
    {
        var request = NewDraft();
        request.DisableExpiration();

        var result = request.MarkExpired(DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.ExpirationDisabled", result.Error.Code);
    }

    [Fact]
    public void Send_without_expiration_leaves_ExpiresAtUtc_null()
    {
        var request = NewInProgressBuilder(expirationEnabled: false);
        Assert.Null(request.ExpiresAtUtc);
    }

    // ================== Hook on sign ==================

    [Fact]
    public void MarkSignerSigned_hooks_partial_copy_when_audience_includes_signer()
    {
        var request = NewInProgressWithPartialCopyFor(out var signer);

        request.MarkSignerSigned(signer.Id, DateTime.UtcNow, null, null);

        Assert.All(signer.DocumentCompletions, completion => Assert.NotNull(completion.PartialCopyRequestedAtUtc));
    }

    [Fact]
    public void MarkSignerSigned_does_not_hook_when_flag_off()
    {
        var request = NewInProgressBuilder(expirationEnabled: true);
        var signer = request.Signers.Single();

        request.MarkSignerSigned(signer.Id, DateTime.UtcNow, null, null);

        Assert.All(signer.DocumentCompletions, completion => Assert.Null(completion.PartialCopyRequestedAtUtc));
    }

    [Fact]
    public void MarkSignerSigned_hook_is_idempotent()
    {
        var request = NewInProgressWithPartialCopyFor(out var signer);
        request.MarkSignerSigned(signer.Id, DateTime.UtcNow, null, null);
        var first = signer.DocumentCompletions.Single().PartialCopyRequestedAtUtc;

        request.MarkSignerSigned(signer.Id, DateTime.UtcNow.AddMinutes(1), null, null);

        Assert.Equal(first, signer.DocumentCompletions.Single().PartialCopyRequestedAtUtc);
    }

    // ================== Record*/Signer transitions ==================

    [Fact]
    public void RecordPartialCopySent_persists_file_and_timestamp()
    {
        var request = NewInProgressWithPartialCopyFor(out var signer);
        request.MarkSignerSigned(signer.Id, DateTime.UtcNow, null, null);
        var fileId = Guid.NewGuid();
        var when = DateTime.UtcNow;

        var result = request.RecordPartialCopySent(signer.Id, fileId, when);

        Assert.True(result.IsSuccess);
        Assert.Equal(fileId, signer.PartialCopyFileId);
        Assert.Equal(when, signer.PartialCopySentAtUtc);
        Assert.Null(signer.PartialCopyFailureReason);
    }

    [Fact]
    public void RecordPartialCopyFailed_sets_reason()
    {
        var request = NewInProgressWithPartialCopyFor(out var signer);
        request.MarkSignerSigned(signer.Id, DateTime.UtcNow, null, null);

        var result = request.RecordPartialCopyFailed(signer.Id, "cloud offline");

        Assert.True(result.IsSuccess);
        Assert.Equal("cloud offline", signer.PartialCopyFailureReason);
    }

    [Fact]
    public void Record_methods_fail_for_unknown_signer()
    {
        var request = NewDraft();

        var sent = request.RecordPartialCopySent(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        var failed = request.RecordPartialCopyFailed(Guid.NewGuid(), "x");

        Assert.True(sent.IsFailure);
        Assert.True(failed.IsFailure);
    }

    // ================== Helpers ==================

    private static SignatureRequest NewDraft() =>
        SignatureRequest
            .CreateDraft(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Partial Copy Test",
                null,
                "Fiscal",
                Guid.NewGuid(),
                tokenExpirationHours: 72,
                requiresSequentialSigning: false,
                requiresConsent: false,
                generateCertificate: false
            )
            .Value;

    private static SignatureRequest NewDraftWithSigner(out Signer signer)
    {
        var request = NewDraft();
        signer = request
            .AddSigner(SignerEmail.Create("alice@example.com").Value, SignerFullName.Create("Alice A").Value, null)
            .Value;
        return request;
    }

    private static SignatureRequest NewInProgressBuilder(bool expirationEnabled)
    {
        var draft = SignatureRequest
            .CreateDraft(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Partial Copy InProgress",
                null,
                "Fiscal",
                Guid.NewGuid(),
                tokenExpirationHours: 48,
                requiresSequentialSigning: false,
                requiresConsent: false,
                generateCertificate: false,
                expirationEnabled: expirationEnabled
            )
            .Value;
        var signer = draft
            .AddSigner(SignerEmail.Create("bob@example.com").Value, SignerFullName.Create("Bob B").Value, null)
            .Value;
        var pos = FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value;
        draft.PlaceField(signer.Id, SignatureFieldKind.Signature, pos, null, false);
        draft.AttachOriginalHash(DocumentHash.Create(new string('a', 64)).Value);
        draft.Send(DateTime.UtcNow);
        return draft;
    }

    private static SignatureRequest NewInProgressWithPartialCopyFor(out Signer signer)
    {
        var request = NewInProgressBuilder(expirationEnabled: true);
        signer = request.Signers.Single();
        request.SetSendPartialCopy(true, PartialCopyAudience.All());
        return request;
    }
}
