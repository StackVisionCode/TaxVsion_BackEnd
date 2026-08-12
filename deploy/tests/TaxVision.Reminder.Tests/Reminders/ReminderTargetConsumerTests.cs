using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Reminder.Application.Reminders.Abstractions;
using TaxVision.Reminder.Application.Reminders.Consumers;
using TaxVision.Reminder.Domain.Reminders;
using TaxVision.Reminder.Domain.ValueObjects;

namespace TaxVision.Reminder.Tests.Reminders;

/// <summary>
/// Las dos reacciones al objetivo. La que importa de verdad es la asimetría de la invariante R6:
/// mover la cita arrastra al recordatorio <b>anclado</b> y deja quieto al <b>absoluto</b>. Si esto
/// se rompe, el bug llega al usuario como «moví la tarea y el aviso se fue con ella cuando yo no
/// quería» — o su inverso, que es peor porque es silencioso.
/// </summary>
public sealed class ReminderTargetConsumerTests
{
    private static readonly Guid TenantId = Guid.Parse("d4879234-7370-4b58-b49c-094bd7c04847");
    private static readonly Guid UserId = Guid.Parse("2b91f0c4-1111-4222-8333-444455556666");
    private static readonly Guid TargetId = Guid.Parse("7f3a1188-3333-4444-8555-666677778888");

    [Fact]
    public async Task TargetMoved_SobreUnAnclado_RecalculaLaHoraYReagenda()
    {
        var reminders = new FakeReminderRepository();
        var scheduler = new RecordingScheduler();
        var nowUtc = DateTime.UtcNow;
        var reminder = Seed(reminders, ReminderSchedule.Anchored(nowUtc.AddHours(2), 30, nowUtc).Value);

        await MoveTarget(reminders, scheduler, nowUtc.AddHours(5));

        Assert.Equal(nowUtc.AddHours(5).AddMinutes(-30), reminder.Schedule.FireAtUtc);
        Assert.Equal(ReminderStatus.Scheduled, reminder.Status);
        Assert.Single(scheduler.Scheduled);
    }

    /// <summary>«El jueves a las 9 pase lo que pase»: el evento se ignora, y eso es éxito, no error.</summary>
    [Fact]
    public async Task TargetMoved_SobreUnAbsoluto_NoCambiaNadaNiTocaElScheduler()
    {
        var reminders = new FakeReminderRepository();
        var scheduler = new RecordingScheduler();
        var nowUtc = DateTime.UtcNow;
        var reminder = Seed(reminders, ReminderSchedule.Absolute(nowUtc.AddHours(2), nowUtc).Value);
        var originalFireAtUtc = reminder.Schedule.FireAtUtc;

        await MoveTarget(reminders, scheduler, nowUtc.AddHours(5));

        Assert.Equal(originalFireAtUtc, reminder.Schedule.FireAtUtc);
        Assert.Empty(scheduler.Scheduled);
        Assert.Empty(scheduler.Unscheduled);
    }

    /// <summary>
    /// El objetivo se movió hacia atrás y el disparo recalculado ya pasó: avisar de algo cuya hora
    /// pasó es ruido, así que queda <c>Missed</c> y hay que <b>sacar</b> el trigger, no moverlo.
    /// </summary>
    [Fact]
    public async Task TargetMoved_HaciaElPasado_QuedaMissedYDesagenda()
    {
        var reminders = new FakeReminderRepository();
        var scheduler = new RecordingScheduler();
        var nowUtc = DateTime.UtcNow;
        var metrics = new RecordingReminderMetrics();
        var reminder = Seed(reminders, ReminderSchedule.Anchored(nowUtc.AddHours(4), 30, nowUtc).Value);

        await MoveTarget(reminders, scheduler, nowUtc.AddMinutes(10), metrics);

        Assert.Equal(ReminderStatus.Missed, reminder.Status);
        Assert.Single(scheduler.Unscheduled);
        Assert.Empty(scheduler.Scheduled);

        // Fase 9: este camino a Missed no es el de la ventana de gracia. Distinguirlos es todo el
        // valor del tag: uno dice "hubo caída", el otro "alguien movió la cita hacia atrás".
        Assert.Equal([ReminderMisfirePolicies.AnchorMovedToPast], metrics.Misfired);
    }

    [Fact]
    public async Task TargetClosed_CancelaLosPendientesYDesagenda()
    {
        var reminders = new FakeReminderRepository();
        var scheduler = new RecordingScheduler();
        var metrics = new RecordingReminderMetrics();
        var nowUtc = DateTime.UtcNow;
        var reminder = Seed(reminders, ReminderSchedule.Absolute(nowUtc.AddHours(2), nowUtc).Value);

        await ReminderTargetClosedConsumer.Handle(
            new ReminderTargetClosedIntegrationEvent
            {
                TenantId = TenantId,
                Category = nameof(ReminderCategory.Task),
                TargetId = TargetId,
                Reason = "completed",
            },
            reminders,
            scheduler,
            new NoOpUnitOfWork(),
            new FixedCorrelationContext(),
            metrics,
            NullLogger<ReminderAggregate>.Instance,
            CancellationToken.None
        );

        Assert.Equal(ReminderStatus.Cancelled, reminder.Status);
        Assert.Equal(ReminderCancellationReasons.TargetClosed, reminder.CancellationReason);
        Assert.Single(scheduler.Unscheduled);
        Assert.Equal([ReminderCancellationReasons.TargetClosed], metrics.Cancelled);
    }

    /// <summary>
    /// Una categoría que este despliegue no conoce se descarta con log. Si lanzara, Wolverine
    /// reintentaría hasta la DLQ un evento que ningún reintento puede arreglar.
    /// </summary>
    [Fact]
    public async Task Categoria_Desconocida_SeDescartaSinTocarNada()
    {
        var reminders = new FakeReminderRepository();
        var scheduler = new RecordingScheduler();
        var metrics = new RecordingReminderMetrics();
        var nowUtc = DateTime.UtcNow;
        var reminder = Seed(reminders, ReminderSchedule.Absolute(nowUtc.AddHours(2), nowUtc).Value);

        await ReminderTargetClosedConsumer.Handle(
            new ReminderTargetClosedIntegrationEvent
            {
                TenantId = TenantId,
                Category = "Invoice",
                TargetId = TargetId,
            },
            reminders,
            scheduler,
            new NoOpUnitOfWork(),
            new FixedCorrelationContext(),
            metrics,
            NullLogger<ReminderAggregate>.Instance,
            CancellationToken.None
        );

        Assert.Equal(ReminderStatus.Scheduled, reminder.Status);
        Assert.Empty(scheduler.Unscheduled);
    }

    /// <summary>
    /// El valor numérico crudo se rechaza a propósito: aceptarlo ataría a los publicadores al orden
    /// de los miembros del enum, que es justo el acoplamiento que el string vino a evitar.
    /// </summary>
    [Fact]
    public async Task Categoria_NumericaCruda_TambienSeDescarta()
    {
        var reminders = new FakeReminderRepository();
        var scheduler = new RecordingScheduler();
        var metrics = new RecordingReminderMetrics();
        var nowUtc = DateTime.UtcNow;
        var reminder = Seed(reminders, ReminderSchedule.Absolute(nowUtc.AddHours(2), nowUtc).Value);

        await ReminderTargetClosedConsumer.Handle(
            new ReminderTargetClosedIntegrationEvent
            {
                TenantId = TenantId,
                Category = "3",
                TargetId = TargetId,
            },
            reminders,
            scheduler,
            new NoOpUnitOfWork(),
            new FixedCorrelationContext(),
            metrics,
            NullLogger<ReminderAggregate>.Instance,
            CancellationToken.None
        );

        Assert.Equal(ReminderStatus.Scheduled, reminder.Status);
        Assert.Empty(scheduler.Unscheduled);
    }

    private static Task MoveTarget(
        FakeReminderRepository reminders,
        RecordingScheduler scheduler,
        DateTime newAnchorAtUtc,
        RecordingReminderMetrics? metrics = null
    ) =>
        ReminderTargetMovedConsumer.Handle(
            new ReminderTargetMovedIntegrationEvent
            {
                TenantId = TenantId,
                Category = nameof(ReminderCategory.Task),
                TargetId = TargetId,
                NewAnchorAtUtc = newAnchorAtUtc,
            },
            reminders,
            scheduler,
            new NoOpUnitOfWork(),
            new FixedCorrelationContext(),
            metrics ?? new RecordingReminderMetrics(),
            NullLogger<ReminderAggregate>.Instance,
            CancellationToken.None
        );

    private static ReminderAggregate Seed(FakeReminderRepository reminders, ReminderSchedule schedule)
    {
        var reminder = ReminderAggregate
            .Create(
                TenantId,
                UserId,
                ReminderSubject.Create("Entregar la declaración", body: null).Value,
                ReminderTarget.Create(ReminderCategory.Task, TargetId).Value,
                schedule,
                ReminderTimeZone.Utc,
                RequestKey.Create("test:target:1").Value,
                DateTime.UtcNow
            )
            .Value;

        reminders.Seed(reminder);
        return reminder;
    }
}
