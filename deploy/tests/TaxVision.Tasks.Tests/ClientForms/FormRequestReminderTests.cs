using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Application.Requests.Commands;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Tests.Dependencies;
using static TaxVision.Tasks.Tests.ClientForms.FormRequestFixtures;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class FormRequestReminderTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
    private static readonly ClientFormsReminderOptions Options = new();

    private static ReminderDecision Evaluate(
        FormRequestStatus status,
        double? dueOffsetDays,
        double? lastOffsetDays,
        int count
    ) =>
        ReminderSchedule.Evaluate(
            status,
            dueOffsetDays is { } due ? Now.AddDays(due) : null,
            lastOffsetDays is { } last ? Now.AddDays(last) : null,
            count,
            Now,
            Options
        );

    [Fact]
    public void Closed_requests_never_remind()
    {
        Assert.False(Evaluate(FormRequestStatus.Completed, -5, null, 0).ShouldSend);
        Assert.False(Evaluate(FormRequestStatus.Cancelled, -5, null, 0).ShouldSend);
        Assert.False(Evaluate(FormRequestStatus.Submitted, -5, null, 0).ShouldSend);
    }

    [Fact]
    public void Without_a_due_date_there_are_no_automatic_reminders()
    {
        var decision = Evaluate(FormRequestStatus.Pending, null, null, 0);
        Assert.False(decision.ShouldSend);
        Assert.False(decision.IsOverdue);
    }

    [Fact]
    public void The_cap_counts_every_reminder()
    {
        Assert.False(Evaluate(FormRequestStatus.Pending, -1, null, 5).ShouldSend);
    }

    [Fact]
    public void Fires_at_the_three_day_stage()
    {
        // Vence en 3 días y nunca se recordó: sale el primero y no es overdue.
        var decision = Evaluate(FormRequestStatus.Pending, 3, null, 0);
        Assert.True(decision.ShouldSend);
        Assert.False(decision.IsOverdue);
    }

    [Fact]
    public void Does_not_fire_twice_within_the_same_stage()
    {
        // Vence en 2 días y ya se recordó hace poco (en la etapa de 3 días): no repite.
        Assert.False(Evaluate(FormRequestStatus.Pending, 2, -0.5, 1).ShouldSend);
    }

    [Fact]
    public void Fires_at_the_one_day_stage_after_the_earlier_reminder()
    {
        // Vence en 1 día; el último recordatorio fue 2 días antes del vencimiento (etapa de 3 días).
        Assert.True(Evaluate(FormRequestStatus.Pending, 1, -1, 1).ShouldSend);
    }

    [Fact]
    public void Fires_once_when_it_becomes_overdue()
    {
        // Venció hace 1 día; el último aviso fue antes del vencimiento.
        var decision = Evaluate(FormRequestStatus.Pending, -1, -2, 2);
        Assert.True(decision.ShouldSend);
        Assert.True(decision.IsOverdue);
    }

    [Fact]
    public void Overdue_waits_the_cadence_between_reminders()
    {
        // Venció hace 2 días y ya se avisó vencida hace 1 día: aún no toca (cadencia 3 días).
        Assert.False(Evaluate(FormRequestStatus.Pending, -2, -1, 3).ShouldSend);
    }

    [Fact]
    public void Overdue_fires_again_after_the_cadence()
    {
        // Venció hace 5 días y el último aviso fue hace 3 días: toca otro.
        var decision = Evaluate(FormRequestStatus.Pending, -5, -3, 3);
        Assert.True(decision.ShouldSend);
        Assert.True(decision.IsOverdue);
    }

    [Fact]
    public void Disabled_options_stop_everything()
    {
        var disabled = new ClientFormsReminderOptions { Enabled = false };
        var decision = ReminderSchedule.Evaluate(FormRequestStatus.Pending, Now.AddDays(-1), null, 0, Now, disabled);
        Assert.False(decision.ShouldSend);
    }

    [Fact]
    public async Task Manual_reminder_records_publishes_and_audits()
    {
        var request = CustomerRequest();
        var bus = new FakeMessageBus();
        var audit = new RecordingFormAuditLog();

        var result = await SendFormRequestReminderHandler.Handle(
            new SendFormRequestReminderCommand(TenantId, UserId, true, request.Id, Now),
            new SingleFormRequestRepository(request),
            new FixedRequestVisibility(true),
            Microsoft.Extensions.Options.Options.Create(new ClientFormsOptions()),
            audit,
            new RecordingUnitOfWork(),
            bus,
            new TestCorrelationContext(),
            default
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(1, request.ReminderCount);
        Assert.Contains("request.reminder_sent", audit.Actions);
        var evt = Assert.IsType<FormRequestReminderDueIntegrationEvent>(Assert.Single(bus.Published));
        Assert.True(evt.IsManual);
        Assert.Equal(1, evt.ReminderNumber);
    }

    [Fact]
    public async Task Manual_reminder_returns_409_past_the_cap()
    {
        var request = CustomerRequest();
        for (var i = 0; i < 5; i++)
            request.RecordReminder(5, Now);

        var result = await SendFormRequestReminderHandler.Handle(
            new SendFormRequestReminderCommand(TenantId, UserId, true, request.Id, Now),
            new SingleFormRequestRepository(request),
            new FixedRequestVisibility(true),
            Microsoft.Extensions.Options.Options.Create(new ClientFormsOptions()),
            new RecordingFormAuditLog(),
            new RecordingUnitOfWork(),
            new FakeMessageBus(),
            new TestCorrelationContext(),
            default
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FormRequestErrors.ReminderLimitReached, result.Error);
    }
}
