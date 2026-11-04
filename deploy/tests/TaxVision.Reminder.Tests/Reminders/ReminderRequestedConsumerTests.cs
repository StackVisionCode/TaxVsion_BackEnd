using BuildingBlocks.Messaging.AuthIntegrationEvents;
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

    [Fact]
    public async Task LaMismaClaveParaOtroDestinatario_CreaSuPropioRecordatorio()
    {
        // Cita reasignada: Calendar vuelve a pedir con la misma clave, ahora para el organizador nuevo.
        var reminders = new FakeReminderRepository();
        var scheduler = new RecordingScheduler();
        var successor = Guid.NewGuid();
        var evt = CalendarOccurrence("calendar:occ-1");

        await Handle(evt, reminders, scheduler);
        await Handle(evt with { UserId = successor }, reminders, scheduler);
        await Handle(evt with { UserId = successor }, reminders, scheduler); // reintento del job

        Assert.Equal(2, reminders.Stored.Count);
        Assert.Single(reminders.Stored, r => r.UserId == UserId);
        Assert.Single(reminders.Stored, r => r.UserId == successor);
        Assert.Equal(2, scheduler.Scheduled.Count);
    }

    [Fact]
    public async Task RetiroAntesDelPedido_ElSucesorRecibeElAvisoYElDelRetiradoNoRevive()
    {
        var reminders = new FakeReminderRepository();
        var scheduler = new RecordingScheduler();
        var successor = Guid.NewGuid();
        var evt = CalendarOccurrence("calendar:occ-2");
        await Handle(evt, reminders, scheduler);

        await Offboard(reminders, scheduler, UserId);
        await Handle(evt, reminders, scheduler); // pasada vieja del job, aún con el retirado
        await Handle(evt with { UserId = successor }, reminders, scheduler);

        Assert.Equal(ReminderStatus.Cancelled, Assert.Single(reminders.Stored, r => r.UserId == UserId).Status);
        Assert.Equal(ReminderStatus.Scheduled, Assert.Single(reminders.Stored, r => r.UserId == successor).Status);
    }

    [Fact]
    public async Task PedidoAntesDelRetiro_ElRetiroSoloCancelaElDelRetirado()
    {
        var reminders = new FakeReminderRepository();
        var scheduler = new RecordingScheduler();
        var successor = Guid.NewGuid();
        var evt = CalendarOccurrence("calendar:occ-3");
        await Handle(evt, reminders, scheduler);

        await Handle(evt with { UserId = successor }, reminders, scheduler);
        await Offboard(reminders, scheduler, UserId);

        Assert.Equal(ReminderStatus.Cancelled, Assert.Single(reminders.Stored, r => r.UserId == UserId).Status);
        Assert.Equal(ReminderStatus.Scheduled, Assert.Single(reminders.Stored, r => r.UserId == successor).Status);
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

    private static Task Offboard(FakeReminderRepository reminders, RecordingScheduler scheduler, Guid leaver) =>
        ReminderUserOffboardedConsumer.Handle(
            new UserOffboardedIntegrationEvent
            {
                TenantId = TenantId,
                UserId = leaver,
                Email = "leaver@example.com",
                ActorType = "TenantEmployee",
                RemovedAtUtc = DateTime.UtcNow,
            },
            reminders,
            scheduler,
            new NoOpUnitOfWork(),
            new FixedCorrelationContext(),
            new RecordingReminderMetrics(),
            NullLogger<ReminderAggregate>.Instance,
            CancellationToken.None
        );

    /// <summary>Como lo pide el job de Calendar: anclado a la ocurrencia, clave sin el usuario.</summary>
    private static ReminderRequestedIntegrationEvent CalendarOccurrence(string requestKey) =>
        new()
        {
            TenantId = TenantId,
            UserId = UserId,
            Category = nameof(ReminderCategory.Calendar),
            TargetId = Guid.NewGuid(),
            Title = "Client review",
            TimeZoneId = "America/New_York",
            AnchorAtUtc = DateTime.UtcNow.AddDays(2),
            LeadMinutes = 30,
            RequestKey = requestKey,
        };

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
