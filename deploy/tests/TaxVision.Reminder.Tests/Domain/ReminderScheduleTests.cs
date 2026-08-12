using TaxVision.Reminder.Domain.ValueObjects;

namespace TaxVision.Reminder.Tests.Domain;

/// <summary>
/// El VO donde vive anclado-vs-absoluto (ADR-R-03). Si esto se rompe, el bug sale en producción
/// como «moví la tarea y el aviso se fue con ella» o su inverso silencioso.
/// </summary>
public sealed class ReminderScheduleTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Anchored_RestaElLeadAlAncla()
    {
        var anchor = NowUtc.AddDays(2);

        var result = ReminderSchedule.Anchored(anchor, leadMinutes: 1_440, NowUtc);

        Assert.True(result.IsSuccess);
        Assert.Equal(anchor.AddMinutes(-1_440), result.Value.FireAtUtc);
        Assert.Equal(anchor, result.Value.AnchorAtUtc);
        Assert.True(result.Value.IsAnchored);
    }

    [Fact]
    public void Anchored_RechazaDisparoEnElPasado()
    {
        // El ancla es futura pero el lead la empuja hacia atrás: el disparo caería antes de ahora (S1).
        var result = ReminderSchedule.Anchored(NowUtc.AddHours(1), leadMinutes: 120, NowUtc);

        Assert.True(result.IsFailure);
        Assert.Equal("Reminder.Schedule.InThePast", result.Error.Code);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(ReminderSchedule.MaxLeadMinutes + 1)]
    public void Anchored_RechazaLeadFueraDeRango(int leadMinutes)
    {
        var result = ReminderSchedule.Anchored(NowUtc.AddDays(400), leadMinutes, NowUtc);

        Assert.True(result.IsFailure);
        Assert.Equal("Reminder.Schedule.LeadOutOfRange", result.Error.Code);
    }

    [Fact]
    public void Anchored_RechazaMasAllaDelHorizonte()
    {
        // S5 — un error de entrada no debe dejar un trigger de Quartz vivo durante 6 años.
        var result = ReminderSchedule.Anchored(NowUtc.AddYears(6), leadMinutes: 0, NowUtc);

        Assert.True(result.IsFailure);
        Assert.Equal("Reminder.Schedule.TooFarInFuture", result.Error.Code);
    }

    [Fact]
    public void Anchored_RechazaAnclaQueNoEsUtc()
    {
        var localAnchor = DateTime.SpecifyKind(NowUtc.AddDays(2), DateTimeKind.Local);

        var result = ReminderSchedule.Anchored(localAnchor, leadMinutes: 0, NowUtc);

        Assert.True(result.IsFailure);
        Assert.Equal("Reminder.Schedule.NotUtc", result.Error.Code);
    }

    [Fact]
    public void Absolute_NoExponeAnclaNiLead()
    {
        var result = ReminderSchedule.Absolute(NowUtc.AddDays(1), NowUtc);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsAnchored);
        Assert.Null(result.Value.AnchorAtUtc);
        Assert.Null(result.Value.LeadMinutes);
    }

    [Fact]
    public void WithNewAnchor_SobreAbsoluto_Falla()
    {
        // S4 a nivel de VO. El par con RescheduleToNewAnchor_SobreAbsoluto_EsNoOpExitoso documenta
        // la diferencia entre las dos capas: el VO no sabe decidir, el aggregate sí.
        var absolute = ReminderSchedule.Absolute(NowUtc.AddDays(1), NowUtc).Value;

        var result = absolute.WithNewAnchor(NowUtc.AddDays(3), NowUtc);

        Assert.True(result.IsFailure);
        Assert.Equal("Reminder.Schedule.NotAnchored", result.Error.Code);
    }

    [Fact]
    public void WithNewAnchor_ConservaElLeadYAceptaDisparoPasado()
    {
        var anchored = ReminderSchedule.Anchored(NowUtc.AddDays(2), leadMinutes: 60, NowUtc).Value;

        // La cita se movió hacia atrás: el disparo recalculado ya pasó. El VO NO falla — devuelve el
        // schedule con FireAtUtc en el pasado y deja la decisión al aggregate.
        var result = anchored.WithNewAnchor(NowUtc.AddMinutes(-30), NowUtc);

        Assert.True(result.IsSuccess);
        Assert.Equal(60, result.Value.LeadMinutes);
        Assert.True(result.Value.FireAtUtc < NowUtc);
    }
}
