using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Tests.Domain;

/// <summary>
/// Tests del aggregate root <see cref="SignatureRequest"/>. Cada test cubre UNA regla
/// concreta; los helpers <c>NewDraft</c>/<c>NewSigner</c> montan escenarios comunes.
/// </summary>
public sealed class SignatureRequestTests
{
    // -------------------- CreateDraft --------------------

    [Fact]
    public void CreateDraft_returns_success_with_defaults()
    {
        var result = NewDraft();

        Assert.True(result.IsSuccess);
        var request = result.Value;
        Assert.Equal(SignatureRequestStatus.Draft, request.Status);
        Assert.Equal(0, request.RevocationEpoch);
        Assert.Empty(request.Signers);
        Assert.True(request.ExpiresAtUtc > request.CreatedAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("A")]
    public void CreateDraft_rejects_invalid_titles(string title)
    {
        var result = SignatureRequest.CreateDraft(
            Guid.NewGuid(),
            Guid.NewGuid(),
            title,
            null,
            SignatureCategory.Fiscal,
            Guid.NewGuid(),
            tokenExpirationHours: 72,
            requiresSequentialSigning: false,
            requiresConsent: false,
            generateCertificate: false
        );

        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(721)]
    public void CreateDraft_rejects_out_of_range_token_expiration(int hours)
    {
        var result = SignatureRequest.CreateDraft(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Consent to Disclose",
            null,
            SignatureCategory.ConsentToDisclose,
            Guid.NewGuid(),
            tokenExpirationHours: hours,
            requiresSequentialSigning: false,
            requiresConsent: false,
            generateCertificate: false
        );

        Assert.True(result.IsFailure);
    }

    // -------------------- AddSigner --------------------

    [Fact]
    public void AddSigner_dedups_by_email()
    {
        var request = NewDraft().Value;
        var first = request.AddSigner(NewEmail("dup@example.com"), NewName("Alice A"), mappedCustomerId: null);
        var second = request.AddSigner(NewEmail("DUP@Example.com"), NewName("Bob B"), mappedCustomerId: null);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsFailure);
        Assert.Equal("Signature.Request.DuplicateSignerEmail", second.Error.Code);
        Assert.Single(request.Signers);
    }

    [Fact]
    public void AddSigner_assigns_incrementing_order()
    {
        var request = NewDraft().Value;
        var a = request.AddSigner(NewEmail("a@example.com"), NewName("Alice"), null).Value;
        var b = request.AddSigner(NewEmail("b@example.com"), NewName("Bob"), null).Value;
        var c = request.AddSigner(NewEmail("c@example.com"), NewName("Carla"), null).Value;

        Assert.Equal(1, a.Order);
        Assert.Equal(2, b.Order);
        Assert.Equal(3, c.Order);
    }

    // -------------------- PlaceField --------------------

    [Fact]
    public void PlaceField_defaults_signature_kind_to_required()
    {
        var request = NewDraft().Value;
        var signer = request.AddSigner(NewEmail("s@example.com"), NewName("Sam"), null).Value;
        var pos = FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value;

        var placement = request.PlaceField(
            signer.Id,
            SignatureFieldKind.Signature,
            pos,
            label: null,
            isRequired: false
        );

        Assert.True(placement.IsSuccess);
        Assert.True(placement.Value.IsRequired);
    }

    [Fact]
    public void PlaceField_fails_when_signer_not_found()
    {
        var request = NewDraft().Value;
        var pos = FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value;

        var placement = request.PlaceField(Guid.NewGuid(), SignatureFieldKind.Signature, pos, null, false);

        Assert.True(placement.IsFailure);
        Assert.Equal("Signature.Request.SignerMissing", placement.Error.Code);
    }

    // -------------------- MarkReadyForSending --------------------

    [Fact]
    public void MarkReadyForSending_transitions_draft_to_ready()
    {
        var request = NewDraft().Value;
        var hash = DocumentHash.Create(new string('a', 64)).Value;

        var result = request.MarkReadyForSending(hash);

        Assert.True(result.IsSuccess);
        Assert.Equal(SignatureRequestStatus.Ready, request.Status);
        Assert.Equal(hash.Value, request.DocumentHashPre!.Value);
    }

    [Fact]
    public void MarkReadyForSending_is_idempotent_only_from_draft()
    {
        var request = NewDraft().Value;
        var hash = DocumentHash.Create(new string('a', 64)).Value;
        request.MarkReadyForSending(hash);

        var second = request.MarkReadyForSending(hash);

        Assert.True(second.IsFailure);
        Assert.Equal("Signature.Request.NotDraft", second.Error.Code);
    }

    // -------------------- Send --------------------

    [Fact]
    public void Send_requires_ready_status()
    {
        var request = NewDraft().Value;
        AddSignerWithSignatureField(request, "s@example.com");

        var result = request.Send(DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.NotReady", result.Error.Code);
    }

    [Fact]
    public void Send_requires_at_least_one_signature_or_initials_field()
    {
        var request = NewReadyDraft();
        request.AddSigner(NewEmail("nofield@example.com"), NewName("No Field"), null);

        var result = request.Send(DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.NoSignatureField", result.Error.Code);
    }

    [Fact]
    public void Send_transitions_ready_to_in_progress_when_ready_with_field()
    {
        var request = NewReadyDraftWithSignatureField("s@example.com");

        var sentAt = DateTime.UtcNow;
        var result = request.Send(sentAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(SignatureRequestStatus.InProgress, request.Status);
        Assert.Equal(sentAt, request.SentAtUtc);
    }

    // -------------------- MarkSignerSigned --------------------

    [Fact]
    public void MarkSignerSigned_completes_when_all_signers_signed()
    {
        var request = NewReadyDraftWithSignatureField("only@example.com");
        request.Send(DateTime.UtcNow);
        var signer = request.Signers.Single();

        var result = request.MarkSignerSigned(signer.Id, DateTime.UtcNow, "127.0.0.1", "ua/1.0");

        Assert.True(result.IsSuccess);
        Assert.Equal(SignatureRequestStatus.Completed, request.Status);
        Assert.True(request.RevocationEpoch > 0);
    }

    [Fact]
    public void MarkSignerSigned_stays_in_progress_while_others_pending()
    {
        var request = NewReadyDraftWithSignatureField("first@example.com");
        var second = request.AddSigner(NewEmail("second@example.com"), NewName("Second"), null).Value;
        var pos = FieldPosition.Create(1, 0.5, 0.5, 0.1, 0.05).Value;
        request.PlaceField(second.Id, SignatureFieldKind.Signature, pos, null, false);
        request.Send(DateTime.UtcNow);

        var first = request.Signers.First(s => s.Email.Value == "first@example.com");
        request.MarkSignerSigned(first.Id, DateTime.UtcNow, null, null);

        Assert.Equal(SignatureRequestStatus.InProgress, request.Status);
    }

    // -------------------- Cancel / Extend / Expire --------------------

    [Fact]
    public void Cancel_bumps_revocation_epoch()
    {
        var request = NewReadyDraftWithSignatureField("s@example.com");
        request.Send(DateTime.UtcNow);
        var initialEpoch = request.RevocationEpoch;

        var result = request.Cancel(DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(SignatureRequestStatus.Canceled, request.Status);
        Assert.Equal(initialEpoch + 1, request.RevocationEpoch);
    }

    [Fact]
    public void ExtendExpiration_bumps_revocation_epoch_and_extends_expiry()
    {
        var request = NewDraft().Value;
        var initial = request.ExpiresAtUtc;

        var result = request.ExtendExpiration(24);

        Assert.True(result.IsSuccess);
        Assert.Equal(initial.AddHours(24), request.ExpiresAtUtc);
        Assert.Equal(1, request.RevocationEpoch);
    }

    [Fact]
    public void MarkExpired_expires_all_pending_signers()
    {
        var request = NewReadyDraftWithSignatureField("s1@example.com");
        var second = request.AddSigner(NewEmail("s2@example.com"), NewName("Second"), null).Value;
        var pos = FieldPosition.Create(1, 0.5, 0.5, 0.1, 0.05).Value;
        request.PlaceField(second.Id, SignatureFieldKind.Signature, pos, null, false);
        request.Send(DateTime.UtcNow);

        var result = request.MarkExpired(DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(SignatureRequestStatus.Expired, request.Status);
        Assert.All(request.Signers, s => Assert.Equal(SignerStatus.Expired, s.Status));
    }

    [Fact]
    public void Cannot_edit_after_send()
    {
        var request = NewReadyDraftWithSignatureField("s@example.com");
        request.Send(DateTime.UtcNow);

        Assert.Throws<InvalidOperationException>(() =>
            request.AddSigner(NewEmail("late@example.com"), NewName("Late"), null)
        );
    }

    // ================== helpers ==================

    private static BuildingBlocks.Results.Result<SignatureRequest> NewDraft() =>
        SignatureRequest.CreateDraft(
            tenantId: Guid.NewGuid(),
            createdByUserId: Guid.NewGuid(),
            title: "Consent to Disclose 2026",
            description: null,
            category: SignatureCategory.ConsentToDisclose,
            originalFileId: Guid.NewGuid(),
            tokenExpirationHours: 72,
            requiresSequentialSigning: false,
            requiresConsent: false,
            generateCertificate: false
        );

    private static SignatureRequest NewReadyDraft()
    {
        var request = NewDraft().Value;
        var hash = DocumentHash.Create(new string('a', 64)).Value;
        request.MarkReadyForSending(hash);
        return request;
    }

    private static SignatureRequest NewReadyDraftWithSignatureField(string email)
    {
        var request = NewReadyDraft();
        AddSignerWithSignatureField(request, email);
        return request;
    }

    private static void AddSignerWithSignatureField(SignatureRequest request, string email)
    {
        var signer = request.AddSigner(NewEmail(email), NewName("Some Name"), null).Value;
        var pos = FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value;
        request.PlaceField(signer.Id, SignatureFieldKind.Signature, pos, null, false);
    }

    private static SignerEmail NewEmail(string raw) => SignerEmail.Create(raw).Value;

    private static SignerFullName NewName(string raw) => SignerFullName.Create(raw).Value;
}
