using BuildingBlocks.Results;
using TaxVision.Reminder.Domain.Reminders;

namespace TaxVision.Reminder.Domain.ValueObjects;

/// <summary>
/// El VO que sostiene todo el modelo: acá vive la decisión <b>anclado vs absoluto</b> (ADR-R-03).
///
/// <para>
/// <b>Anclado</b> («un día antes del vencimiento») recuerda su ancla y su lead, así que puede
/// recalcularse cuando el objetivo se mueve. <b>Absoluto</b> («el jueves a las 9, pase lo que
/// pase») no guarda ancla y por definición ignora que el objetivo se mueva.
/// </para>
///
/// <para>
/// Si esto se modela mal, el bug aparece en producción como «moví la tarea y el aviso se fue con
/// ella cuando yo no quería» — o su inverso, que es peor porque es silencioso.
/// </para>
/// </summary>
public sealed record ReminderSchedule
{
    /// <summary>Tope de cordura para los triggers de Quartz: 5 años (S5).</summary>
    public const int MaxHorizonYears = 5;

    /// <summary>Un año en minutos — tope de <see cref="LeadMinutes"/> (S2).</summary>
    public const int MaxLeadMinutes = 525_600;

    private ReminderSchedule(DateTime fireAtUtc, DateTime? anchorAtUtc, int? leadMinutes)
    {
        FireAtUtc = fireAtUtc;
        AnchorAtUtc = anchorAtUtc;
        LeadMinutes = leadMinutes;
    }

    public DateTime FireAtUtc { get; }
    public DateTime? AnchorAtUtc { get; }
    public int? LeadMinutes { get; }

    public bool IsAnchored => AnchorAtUtc is not null;

    /// <summary>«Un día antes del vencimiento» — se recalcula si el objetivo se mueve.</summary>
    public static Result<ReminderSchedule> Anchored(DateTime anchorAtUtc, int leadMinutes, DateTime nowUtc)
    {
        if (anchorAtUtc.Kind != DateTimeKind.Utc)
            return Result.Failure<ReminderSchedule>(ReminderErrors.Schedule.NotUtc);

        if (leadMinutes < 0 || leadMinutes > MaxLeadMinutes)
            return Result.Failure<ReminderSchedule>(ReminderErrors.Schedule.LeadOutOfRange);

        var fireAtUtc = anchorAtUtc.AddMinutes(-leadMinutes);
        var horizonCheck = EnsureCreatable(fireAtUtc, nowUtc);
        if (horizonCheck.IsFailure)
            return Result.Failure<ReminderSchedule>(horizonCheck.Error);

        return Result.Success(new ReminderSchedule(fireAtUtc, anchorAtUtc, leadMinutes));
    }

    /// <summary>«El jueves a las 9, pase lo que pase» — ignora que el objetivo se mueva.</summary>
    public static Result<ReminderSchedule> Absolute(DateTime fireAtUtc, DateTime nowUtc)
    {
        if (fireAtUtc.Kind != DateTimeKind.Utc)
            return Result.Failure<ReminderSchedule>(ReminderErrors.Schedule.NotUtc);

        var horizonCheck = EnsureCreatable(fireAtUtc, nowUtc);
        if (horizonCheck.IsFailure)
            return Result.Failure<ReminderSchedule>(horizonCheck.Error);

        return Result.Success(new ReminderSchedule(fireAtUtc, anchorAtUtc: null, leadMinutes: null));
    }

    /// <summary>
    /// Recalcula contra un ancla nueva. Falla si el schedule <b>no</b> es anclado (S4): el llamador
    /// debe preguntar por <see cref="IsAnchored"/> antes, y a nivel de aggregate un absoluto que
    /// recibe <c>target_moved</c> es un no-op exitoso, no un error (invariante R6).
    ///
    /// <para>
    /// <b>No valida que el disparo sea futuro, a propósito.</b> Si la cita se movió hacia atrás y el
    /// disparo recalculado ya pasó, devuelve un schedule con <see cref="FireAtUtc"/> en el pasado y
    /// es el <b>aggregate</b> quien decide qué hacer (transicionar a <c>Missed</c> en vez de
    /// reagendar). La validación de futuro es sólo del alta.
    /// </para>
    /// </summary>
    public Result<ReminderSchedule> WithNewAnchor(DateTime newAnchorAtUtc, DateTime nowUtc)
    {
        if (!IsAnchored)
            return Result.Failure<ReminderSchedule>(ReminderErrors.Schedule.NotAnchored);

        if (newAnchorAtUtc.Kind != DateTimeKind.Utc)
            return Result.Failure<ReminderSchedule>(ReminderErrors.Schedule.NotUtc);

        var lead = LeadMinutes!.Value;
        return Result.Success(new ReminderSchedule(newAnchorAtUtc.AddMinutes(-lead), newAnchorAtUtc, lead));
    }

    /// <summary>S1 + S5 — las dos reglas que sólo aplican al crear un schedule desde cero.</summary>
    private static Result EnsureCreatable(DateTime fireAtUtc, DateTime nowUtc)
    {
        if (fireAtUtc <= nowUtc)
            return Result.Failure(ReminderErrors.Schedule.InThePast);

        if (fireAtUtc > nowUtc.AddYears(MaxHorizonYears))
            return Result.Failure(ReminderErrors.Schedule.TooFarInFuture);

        return Result.Success();
    }
}
