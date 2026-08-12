using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Reminder.Application.Reminders.Consumers;
using TaxVision.Reminder.Domain.Reminders;
using TaxVision.Reminder.Domain.ValueObjects;

namespace TaxVision.Reminder.Tests.Reminders;

/// <summary>
/// La idempotencia por <c>RequestKey</c> tiene que valer también por el bus, donde un reintento es
/// rutina y no excepción. Es la razón de que el consumer delegue en <c>CreateReminderHandler</c> en
/// vez de construir su propio aggregate.
/// </summary>
public sealed class ReminderRequestedConsumerTests
{
    private static readonly Guid TenantId = Guid.Parse("d4879234-7370-4b58-b49c-094bd7c04847");
    private static readonly Guid UserId = Guid.Parse("2b91f0c4-1111-4222-8333-444455556666");

    [Fact]
    public async Task ElMismoEventoDosVeces_CreaUnSoloRecordatorio()
    {
        var reminders = new FakeReminderRepository();
        var scheduler = new RecordingScheduler();
        var evt = Requested("task-created:7f3a:2b91");

        await Handle(evt, reminders, scheduler);
        await Handle(evt, reminders, scheduler);

        Assert.Single(reminders.Stored);
        Assert.Single(scheduler.Scheduled);
    }

    [Fact]
    public async Task UnEventoInvalido_SeDescartaSinCrearNada()
    {
        var reminders = new FakeReminderRepository();
        var scheduler = new RecordingScheduler();

        // Category = Task exige TargetId (invariante T2); el aggregate lo rechaza y el consumer no
        // debe reintentarlo: reintentar no lo va a hacer válido.
        var evt = Requested("task-created:sin-target") with
        {
            Category = nameof(ReminderCategory.Task),
        };

        await Handle(evt, reminders, scheduler);

        Assert.Empty(reminders.Stored);
        Assert.Empty(scheduler.Scheduled);
    }

    private static Task Handle(
        ReminderRequestedIntegrationEvent evt,
        FakeReminderRepository reminders,
        RecordingScheduler scheduler
    ) =>
        ReminderRequestedConsumer.Handle(
            evt,
            reminders,
            scheduler,
            reminders.AsUnitOfWork(),
            new FixedCorrelationContext(),
            new RecordingReminderMetrics(),
            NullLogger<ReminderAggregate>.Instance,
            CancellationToken.None
        );

    private static ReminderRequestedIntegrationEvent Requested(string requestKey) =>
        new()
        {
            TenantId = TenantId,
            UserId = UserId,
            Category = nameof(ReminderCategory.General),
            Title = "Llamar a Pérez",
            TimeZoneId = "America/Santo_Domingo",
            FireAtUtc = DateTime.UtcNow.AddHours(2),
            RequestKey = requestKey,
        };
}
