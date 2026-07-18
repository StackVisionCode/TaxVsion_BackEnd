using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Tests.Domain;

public sealed class ReminderAndLegalHoldTests
{
    // -------------------- RecordReminderDispatched --------------------

    [Fact]
    public void RecordReminderDispatched_only_in_progress()
    {
        var draft = NewDraft();
        var result = draft.RecordReminderDispatched(DateTime.UtcNow);
        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.NotInProgress", result.Error.Code);
    }

    [Fact]
    public void RecordReminderDispatched_increments_counter_and_stamps_timestamp()
    {
        var request = NewInProgress();
        var before = request.RemindersSent;

        var r = request.RecordReminderDispatched(DateTime.UtcNow);

        Assert.True(r.IsSuccess);
        Assert.Equal(before + 1, request.RemindersSent);
        Assert.NotNull(request.LastReminderSentAtUtc);
    }

    // -------------------- LegalHold --------------------

    [Fact]
    public void PlaceLegalHold_sets_state()
    {
        var request = NewInProgress();
        var userId = Guid.NewGuid();

        var r = request.PlaceLegalHold(userId, "Subpoena #IRS-2026-4421");

        Assert.True(r.IsSuccess);
        Assert.True(request.LegalHold);
        Assert.Equal("Subpoena #IRS-2026-4421", request.LegalHoldReason);
        Assert.Equal(userId, request.LegalHoldPlacedByUserId);
    }

    [Fact]
    public void PlaceLegalHold_requires_reason()
    {
        var request = NewInProgress();
        var r = request.PlaceLegalHold(Guid.NewGuid(), "");
        Assert.True(r.IsFailure);
        Assert.Equal("Signature.Request.LegalHoldReason", r.Error.Code);
    }

    [Fact]
    public void LiftLegalHold_flips_flag_and_records_user()
    {
        var request = NewInProgress();
        request.PlaceLegalHold(Guid.NewGuid(), "Reason");

        var lifter = Guid.NewGuid();
        var r = request.LiftLegalHold(lifter);

        Assert.True(r.IsSuccess);
        Assert.False(request.LegalHold);
        Assert.Equal(lifter, request.LegalHoldLiftedByUserId);
        Assert.NotNull(request.LegalHoldLiftedAtUtc);
    }

    [Fact]
    public void LiftLegalHold_is_idempotent_when_not_held()
    {
        var request = NewInProgress();
        var r = request.LiftLegalHold(Guid.NewGuid());
        Assert.True(r.IsSuccess);
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

    private static SignatureRequest NewInProgress()
    {
        var draft = NewDraft();
        var signer = draft
            .AddSigner(SignerEmail.Create("s@example.com").Value, SignerFullName.Create("Signer").Value, null)
            .Value;
        var pos = FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value;
        draft.PlaceField(signer.Id, SignatureFieldKind.Signature, pos, null, false);
        draft.MarkReadyForSending(DocumentHash.Create(new string('a', 64)).Value);
        draft.Send(DateTime.UtcNow);
        return draft;
    }
}
