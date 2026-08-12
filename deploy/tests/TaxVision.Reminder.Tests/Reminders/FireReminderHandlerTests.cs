using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Reminder.Application.Reminders.Abstractions;
using TaxVision.Reminder.Application.Reminders.Commands;
using TaxVision.Reminder.Domain.Reminders;
using TaxVision.Reminder.Domain.ValueObjects;

namespace TaxVision.Reminder.Tests.Reminders;

/// <summary>
/// El disparo y su <c>reminder.due.v1</c>. Lo que se prueba acá no se ve en un E2E feliz: que el
/// evento salga <b>una sola vez</b> aunque Quartz ejecute el trigger dos, y que un disparo
/// descartado por llegar tarde <b>no</b> publique nada.
/// </summary>
public sealed class FireReminderHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("d4879234-7370-4b58-b49c-094bd7c04847");
    private static readonly Guid UserId = Guid.Parse("2b91f0c4-1111-4222-8333-444455556666");
    private static readonly TimeSpan Grace = TimeSpan.FromHours(1);

    [Fact]
    public async Task Disparar_DentroDeLaGracia_PublicaElEventoConElContenidoDelRecordatorio()
    {
        var reminders = new FakeReminderRepository();
        var bus = new RecordingMessageBus();
        var metrics = new RecordingReminderMetrics();
        var reminder = Seed(reminders, DateTime.UtcNow.AddMinutes(-1));

        await Fire(reminder, reminders, bus, metrics);

        Assert.Equal(ReminderStatus.Fired, reminder.Status);
        Assert.Equal([ReminderCategory.General], metrics.Fired);
        Assert.Single(metrics.FireDelaysSeconds);
        var published = Assert.IsType<ReminderDueIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(reminder.Id, published.ReminderId);
        Assert.Equal(UserId, published.UserId);
        Assert.Equal(TenantId, published.TenantId);
        Assert.Equal("General", published.Category);
        Assert.Equal("Llamar a Pérez", published.Title);
        Assert.Equal("America/Santo_Domingo", published.TimeZoneId);
        Assert.Equal(0, published.SnoozeCount);
    }

    /// <summary>
    /// En un failover de cluster el mismo trigger puede ejecutarse dos veces. <c>MarkFired</c> es
    /// idempotente, así que el estado no cambia — lo que hay que garantizar es que tampoco salga un
    /// segundo evento, o el usuario recibe el mismo aviso duplicado.
    /// </summary>
    [Fact]
    public async Task Disparar_DosVeces_PublicaUnSoloEvento()
    {
        var reminders = new FakeReminderRepository();
        var bus = new RecordingMessageBus();
        var metrics = new RecordingReminderMetrics();
        var reminder = Seed(reminders, DateTime.UtcNow.AddMinutes(-1));

        await Fire(reminder, reminders, bus, metrics);
        await Fire(reminder, reminders, bus, metrics);

        Assert.Equal(ReminderStatus.Fired, reminder.Status);
        Assert.Single(bus.Published);

        // La segunda ejecución no publica y tampoco cuenta: si contara, el panel mostraría el doble
        // de disparos en cada failover de cluster.
        Assert.Single(metrics.Fired);
    }

    [Fact]
    public async Task Disparar_PasadaLaGracia_QuedaMissedYNoPublicaNada()
    {
        var reminders = new FakeReminderRepository();
        var bus = new RecordingMessageBus();
        var metrics = new RecordingReminderMetrics();
        var reminder = Seed(reminders, DateTime.UtcNow.AddHours(-3));

        await Fire(reminder, reminders, bus, metrics);

        Assert.Equal(ReminderStatus.Missed, reminder.Status);
        Assert.Empty(bus.Published);
        Assert.Equal([ReminderMisfirePolicies.GraceExceeded], metrics.Misfired);
        Assert.Empty(metrics.Fired);
    }

    [Fact]
    public async Task Disparar_UnRecordatorioQueYaNoExiste_NoRevientaNiPublica()
    {
        var reminders = new FakeReminderRepository();
        var bus = new RecordingMessageBus();

        await FireReminderHandler.Handle(
            new FireReminderCommand(TenantId, Guid.NewGuid(), Grace),
            reminders,
            new NoOpUnitOfWork(),
            bus,
            new FixedCorrelationContext(),
            new RecordingReminderMetrics(),
            NullLogger<ReminderAggregate>.Instance,
            CancellationToken.None
        );

        Assert.Empty(bus.Published);
    }

    private static Task Fire(
        ReminderAggregate reminder,
        FakeReminderRepository reminders,
        RecordingMessageBus bus,
        RecordingReminderMetrics? metrics = null
    ) =>
        FireReminderHandler.Handle(
            new FireReminderCommand(reminder.TenantId, reminder.Id, Grace),
            reminders,
            new NoOpUnitOfWork(),
            bus,
            new FixedCorrelationContext(),
            metrics ?? new RecordingReminderMetrics(),
            NullLogger<ReminderAggregate>.Instance,
            CancellationToken.None
        );

    private static ReminderAggregate Seed(FakeReminderRepository reminders, DateTime fireAtUtc)
    {
        // El horizonte del VO rechaza una hora pasada, así que el recordatorio se crea vigente y se
        // "envejece" moviendo su schedule con el mismo método que usa la API.
        var nowUtc = fireAtUtc.AddMinutes(-5);
        var reminder = ReminderAggregate
            .Create(
                TenantId,
                UserId,
                ReminderSubject.Create("Llamar a Pérez", body: null).Value,
                ReminderTarget.Create(ReminderCategory.General, targetId: null).Value,
                ReminderSchedule.Absolute(fireAtUtc, nowUtc).Value,
                ReminderTimeZone.Create("America/Santo_Domingo").Value,
                RequestKey.Create("test:fire:1").Value,
                nowUtc
            )
            .Value;

        reminders.Seed(reminder);
        return reminder;
    }
}
