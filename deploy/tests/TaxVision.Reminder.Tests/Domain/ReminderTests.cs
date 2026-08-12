using TaxVision.Reminder.Domain.Reminders;
using TaxVision.Reminder.Domain.Reminders.Events;
using TaxVision.Reminder.Domain.ValueObjects;

namespace TaxVision.Reminder.Tests.Domain;

/// <summary>
/// El aggregate y su máquina de estados. Ningún método recibe un <c>ReminderStatus</c>: cada
/// transición tiene el suyo, con su validación de origen y su evento.
/// </summary>
public sealed class ReminderTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TenantId = Guid.Parse("d4879234-7370-4b58-b49c-094bd7c04847");
    private static readonly Guid UserId = Guid.Parse("2b91f0c4-1111-4222-8333-444455556666");

    [Fact]
    public void Create_ExigeTenantYUsuario()
    {
        var result = Build(tenantId: Guid.Empty);

        Assert.True(result.IsFailure);
        Assert.Equal("Reminder.OwnerRequired", result.Error.Code);
    }

    [Fact]
    public void Create_NaceScheduledYEmiteElEventoQueAgendaEnQuartz()
    {
        var reminder = Build().Value;

        Assert.Equal(ReminderStatus.Scheduled, reminder.Status);
        Assert.Equal(TenantId, reminder.TenantId);
        Assert.Single(reminder.DomainEvents.OfType<ReminderScheduledDomainEvent>());
    }

    [Fact]
    public void MarkFired_DosVeces_EsIdempotenteYEmiteUnSoloEvento()
    {
        // Un failover de cluster puede ejecutar el mismo trigger dos veces. Si esto no fuera
        // idempotente, el usuario recibiría reminder.due.v1 duplicado.
        var reminder = Build().Value;

        var first = reminder.MarkFired(NowUtc.AddHours(2));
        var second = reminder.MarkFired(NowUtc.AddHours(2));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Single(reminder.DomainEvents.OfType<ReminderFiredDomainEvent>());
    }

    [Fact]
    public void Snooze_DesdeScheduled_Falla()
    {
        var reminder = Build().Value;

        var result = reminder.Snooze(TimeSpan.FromMinutes(10), NowUtc);

        Assert.True(result.IsFailure);
        Assert.Equal("Reminder.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public void Snooze_DesdeFired_DejaElScheduleAbsoluto()
    {
        // Un snooze rompe el anclaje a propósito: el usuario pidió «en 10 minutos», no «10 minutos
        // antes de la cita».
        var reminder = Build().Value;
        reminder.MarkFired(NowUtc.AddHours(2));

        var result = reminder.Snooze(TimeSpan.FromMinutes(10), NowUtc.AddHours(2));

        Assert.True(result.IsSuccess);
        Assert.Equal(ReminderStatus.Snoozed, reminder.Status);
        Assert.False(reminder.Schedule.IsAnchored);
        Assert.Equal(NowUtc.AddHours(2).AddMinutes(10), reminder.Schedule.FireAtUtc);
        Assert.Equal(1, reminder.SnoozeCount);
    }

    [Fact]
    public void Snooze_NumeroOnce_AlcanzaElTope()
    {
        var reminder = Build().Value;
        var clock = NowUtc.AddHours(2);

        for (var i = 0; i < ReminderAggregate.MaxSnoozeCount; i++)
        {
            reminder.MarkFired(clock);
            Assert.True(reminder.Snooze(TimeSpan.FromMinutes(5), clock).IsSuccess);
            clock = clock.AddMinutes(5);
        }

        reminder.MarkFired(clock);
        var eleventh = reminder.Snooze(TimeSpan.FromMinutes(5), clock);

        Assert.True(eleventh.IsFailure);
        Assert.Equal("Reminder.SnoozeLimitReached", eleventh.Error.Code);
    }

    [Fact]
    public void RescheduleToNewAnchor_SobreAbsoluto_EsNoOpExitoso()
    {
        // Invariante R6. A nivel de VO esto falla (S4); a nivel de aggregate es éxito, porque el
        // consumer de target_moved no debe romperse por un caso correcto por diseño.
        var reminder = Build(schedule: ReminderSchedule.Absolute(NowUtc.AddDays(1), NowUtc).Value).Value;
        var originalFireAt = reminder.Schedule.FireAtUtc;

        var result = reminder.RescheduleToNewAnchor(NowUtc.AddDays(5), NowUtc);

        Assert.True(result.IsSuccess);
        Assert.Equal(originalFireAt, reminder.Schedule.FireAtUtc);
        Assert.Equal(ReminderStatus.Scheduled, reminder.Status);
        Assert.Empty(reminder.DomainEvents.OfType<ReminderRescheduledDomainEvent>());
    }

    [Fact]
    public void RescheduleToNewAnchor_ConAnclaMovidaHaciaAtras_TerminaEnMissed()
    {
        // La cita se adelantó tanto que el aviso ya debería haber sonado. Avisar de algo cuya hora
        // pasó es ruido: se descarta y queda la métrica.
        var reminder = Build().Value;

        var result = reminder.RescheduleToNewAnchor(NowUtc.AddMinutes(-5), NowUtc);

        Assert.True(result.IsSuccess);
        Assert.Equal(ReminderStatus.Missed, reminder.Status);
        Assert.Equal(NowUtc, reminder.ResolvedAtUtc);
        Assert.Single(reminder.DomainEvents.OfType<ReminderMissedDomainEvent>());
    }

    [Fact]
    public void RescheduleToNewAnchor_HaciaAdelante_ReagendaYEmiteRescheduled()
    {
        var reminder = Build().Value;

        var result = reminder.RescheduleToNewAnchor(NowUtc.AddDays(5), NowUtc);

        Assert.True(result.IsSuccess);
        Assert.Equal(ReminderStatus.Scheduled, reminder.Status);
        Assert.Equal(NowUtc.AddDays(5).AddMinutes(-60), reminder.Schedule.FireAtUtc);
        Assert.Single(reminder.DomainEvents.OfType<ReminderRescheduledDomainEvent>());
    }

    [Fact]
    public void Cancel_DesdeTerminal_Falla()
    {
        var reminder = Build().Value;
        reminder.MarkFired(NowUtc.AddHours(2));
        reminder.Dismiss(NowUtc.AddHours(2));

        var result = reminder.Cancel("user_request", NowUtc.AddHours(3));

        Assert.True(result.IsFailure);
        Assert.Equal("Reminder.InvalidTransition", result.Error.Code);
        Assert.Equal(ReminderStatus.Dismissed, reminder.Status);
    }

    [Fact]
    public void Cancel_ExigeRazon()
    {
        var reminder = Build().Value;

        var result = reminder.Cancel("   ", NowUtc);

        Assert.True(result.IsFailure);
        Assert.Equal("Reminder.CancellationReasonRequired", result.Error.Code);
    }

    private static BuildingBlocks.Results.Result<ReminderAggregate> Build(
        Guid? tenantId = null,
        ReminderSchedule? schedule = null
    ) =>
        ReminderAggregate.Create(
            tenantId ?? TenantId,
            UserId,
            ReminderSubject.Create("Llamar a Pérez", body: null).Value,
            ReminderTarget.Create(ReminderCategory.General, targetId: null).Value,
            schedule ?? ReminderSchedule.Anchored(NowUtc.AddDays(2), leadMinutes: 60, NowUtc).Value,
            ReminderTimeZone.Create("America/Santo_Domingo").Value,
            RequestKey.Create("test:reminder:1").Value,
            NowUtc
        );
}
