using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Tests.Domain;

/// <summary>F3 — Transiciones Draft → Scheduled → InProgress / Draft.</summary>
public sealed class SignatureRequestScheduleSendTests
{
    [Fact]
    public void ScheduleSend_transitions_draft_to_scheduled()
    {
        var request = ReadyDraftWithField();
        var when = DateTime.UtcNow.AddHours(2);

        var result = request.ScheduleSend(when, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(SignatureRequestStatus.Scheduled, request.Status);
        Assert.Equal(when, request.ScheduledSendAtUtc);
    }

    [Fact]
    public void ScheduleSend_rejects_time_in_the_past()
    {
        var request = ReadyDraftWithField();
        var past = DateTime.UtcNow.AddMinutes(-5);

        var result = request.ScheduleSend(past, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.ScheduleInPast", result.Error.Code);
    }

    [Fact]
    public void ScheduleSend_rejects_when_hash_missing()
    {
        var request = DraftWithSignerAndField();
        var future = DateTime.UtcNow.AddHours(1);

        var result = request.ScheduleSend(future, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.NoDocumentHash", result.Error.Code);
    }

    [Fact]
    public void ScheduleSend_from_Scheduled_updates_the_time()
    {
        // Re-schedule: cambiar la hora de un Scheduled sin cancelar+programar.
        var request = ReadyDraftWithField();
        var first = DateTime.UtcNow.AddHours(1);
        request.ScheduleSend(first, DateTime.UtcNow);
        var second = DateTime.UtcNow.AddHours(5);

        var result = request.ScheduleSend(second, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(SignatureRequestStatus.Scheduled, request.Status);
        Assert.Equal(second, request.ScheduledSendAtUtc);
    }

    [Fact]
    public void CancelSchedule_transitions_scheduled_to_draft()
    {
        var request = ReadyDraftWithField();
        request.ScheduleSend(DateTime.UtcNow.AddHours(1), DateTime.UtcNow);

        var result = request.CancelSchedule();

        Assert.True(result.IsSuccess);
        Assert.Equal(SignatureRequestStatus.Draft, request.Status);
        Assert.Null(request.ScheduledSendAtUtc);
    }

    [Fact]
    public void CancelSchedule_rejects_when_not_scheduled()
    {
        var request = ReadyDraftWithField();

        var result = request.CancelSchedule();

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.NotScheduled", result.Error.Code);
    }

    [Fact]
    public void Send_from_Scheduled_clears_scheduled_time_and_moves_to_InProgress()
    {
        var request = ReadyDraftWithField();
        request.ScheduleSend(DateTime.UtcNow.AddHours(1), DateTime.UtcNow);
        var sentAt = DateTime.UtcNow.AddHours(1).AddMinutes(1);

        var result = request.Send(sentAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(SignatureRequestStatus.InProgress, request.Status);
        Assert.Equal(sentAt, request.SentAtUtc);
        Assert.Null(request.ScheduledSendAtUtc);
    }

    [Fact]
    public void Editing_a_Scheduled_request_is_blocked()
    {
        var request = ReadyDraftWithField();
        request.ScheduleSend(DateTime.UtcNow.AddHours(1), DateTime.UtcNow);

        // AddSigner usa EnsureCanBeEdited por debajo → debería bloquear con el código específico.
        var attempt = request.AddSigner(
            SignerEmail.Create("other@example.com").Value,
            SignerFullName.Create("Other Signer").Value,
            null
        );

        Assert.True(attempt.IsFailure);
        Assert.Equal("Signature.Request.Scheduled", attempt.Error.Code);
    }

    // ============== helpers ==============

    private static SignatureRequest Draft() =>
        SignatureRequest
            .CreateDraft(
                tenantId: Guid.NewGuid(),
                createdByUserId: Guid.NewGuid(),
                title: "Schedule test",
                description: null,
                category: "ConsentToDisclose",
                originalFileId: Guid.NewGuid(),
                tokenExpirationHours: 72,
                requiresSequentialSigning: false,
                requiresConsent: false,
                generateCertificate: false
            )
            .Value;

    private static SignatureRequest DraftWithSignerAndField()
    {
        var request = Draft();
        var signer = request
            .AddSigner(SignerEmail.Create("s@example.com").Value, SignerFullName.Create("Sam Signer").Value, null)
            .Value;
        var position = FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value;
        request.PlaceField(signer.Id, SignatureFieldKind.Signature, position, null, false);
        return request;
    }

    private static SignatureRequest ReadyDraftWithField()
    {
        var request = DraftWithSignerAndField();
        request.AttachOriginalHash(DocumentHash.Create(new string('a', 64)).Value);
        return request;
    }
}
