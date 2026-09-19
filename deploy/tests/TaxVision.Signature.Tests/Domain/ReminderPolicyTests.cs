using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Tests.Domain;

/// <summary>
/// Recordatorios dinámicos: intervalo por-solicitud, on/off, y la regla de "a quién le toca"
/// (<see cref="SignatureRequest.IsReminderDue"/>) medida desde el envío o el último reminder.
/// </summary>
public sealed class ReminderPolicyTests
{
    [Fact]
    public void Defaults_enabled_with_configured_interval()
    {
        var request = NewInProgress(sentAt: DateTime.UtcNow, autoReminders: true, intervalHours: 48);

        Assert.True(request.AutoRemindersEnabled);
        Assert.Equal(48, request.ReminderIntervalHours);
    }

    [Fact]
    public void Not_due_before_the_interval_elapses()
    {
        var now = DateTime.UtcNow;
        var request = NewInProgress(sentAt: now.AddHours(-10), autoReminders: true, intervalHours: 48);

        Assert.False(request.IsReminderDue(now));
    }

    [Fact]
    public void Due_after_the_interval_from_send()
    {
        var now = DateTime.UtcNow;
        var request = NewInProgress(sentAt: now.AddHours(-49), autoReminders: true, intervalHours: 48);

        Assert.True(request.IsReminderDue(now));
    }

    [Fact]
    public void Not_due_when_disabled()
    {
        var now = DateTime.UtcNow;
        var request = NewInProgress(sentAt: now.AddHours(-100), autoReminders: false, intervalHours: 48);

        Assert.False(request.IsReminderDue(now));
    }

    [Fact]
    public void Due_uses_last_reminder_as_baseline()
    {
        var now = DateTime.UtcNow;
        var request = NewInProgress(sentAt: now.AddHours(-200), autoReminders: true, intervalHours: 24);
        request.RecordReminderDispatched(now.AddHours(-10)); // último hace 10h, intervalo 24h

        Assert.False(request.IsReminderDue(now));
    }

    [Fact]
    public void SetReminderPolicy_updates_in_draft_and_validates_range()
    {
        var request = NewDraft();

        Assert.True(request.SetReminderPolicy(true, 72).IsSuccess);
        Assert.Equal(72, request.ReminderIntervalHours);

        var bad = request.SetReminderPolicy(true, 0);
        Assert.True(bad.IsFailure);
        Assert.Equal("Signature.Request.ReminderInterval", bad.Error.Code);
    }

    private static SignatureRequest NewDraft() =>
        SignatureRequest
            .CreateDraft(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Test",
                null,
                SignatureCategory.Fiscal,
                Guid.NewGuid(),
                tokenExpirationHours: 720,
                requiresSequentialSigning: false,
                requiresConsent: false,
                generateCertificate: false
            )
            .Value;

    private static SignatureRequest NewInProgress(DateTime sentAt, bool autoReminders, int intervalHours)
    {
        var draft = SignatureRequest
            .CreateDraft(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Test",
                null,
                SignatureCategory.Fiscal,
                Guid.NewGuid(),
                tokenExpirationHours: 720, // no expira en el rango de estas pruebas
                requiresSequentialSigning: false,
                requiresConsent: false,
                generateCertificate: false,
                autoRemindersEnabled: autoReminders,
                reminderIntervalHours: intervalHours
            )
            .Value;
        var signer = draft
            .AddSigner(SignerEmail.Create("s@example.com").Value, SignerFullName.Create("The Signer").Value, null)
            .Value;
        var pos = FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value;
        draft.PlaceField(signer.Id, SignatureFieldKind.Signature, pos, null, false);
        draft.MarkReadyForSending(DocumentHash.Create(new string('a', 64)).Value);
        draft.Send(sentAt);
        return draft;
    }
}
