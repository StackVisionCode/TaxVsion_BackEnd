using BuildingBlocks.Messaging.TenantIntegrationEvents;
using TaxVision.Tenant.Application.Compliance.Abstractions;
using TaxVision.Tenant.Domain.Compliance;

namespace TaxVision.Tenant.Application.Compliance.Reminders;

/// <summary>
/// Un paso del job diario del WISP para una oficina: el agregado decide si hoy (en su zona horaria) toca
/// avisar y lo anota. Devuelve el aviso; la unidad del job lo confirma junto con la fila.
/// </summary>
public static class WispReviewReminders
{
    /// <summary>El aviso a publicar, o null si hoy no tocaba.</summary>
    public static async Task<OfficeWispReviewDueIntegrationEvent?> RegisterIfDueAsync(
        WispReminderCandidate candidate,
        DateTime nowUtc,
        string correlationId,
        IOfficeComplianceRepository compliances,
        CancellationToken ct
    )
    {
        var compliance = await compliances.GetAsync(candidate.TenantId, ct);
        if (compliance is null)
            return null;

        var today = WispReviewSchedule.OfficeToday(nowUtc, candidate.TimeZoneId);
        var reminder = compliance.RegisterWispReviewReminder(today);
        if (reminder is null)
            return null;

        return new OfficeWispReviewDueIntegrationEvent
        {
            TenantId = compliance.TenantId,
            OfficeTenantId = compliance.TenantId,
            DueOn = reminder.DueOn,
            Stage = ToContract(reminder.Stage),
            SecurityCoordinatorUserId = compliance.SecurityCoordinatorUserId,
            CorrelationId = correlationId,
        };
    }

    /// <summary>
    /// Fecha base más reciente que puede tener un candidato hoy en cualquier zona: la que vence dentro de la
    /// ventana de "pronto" en la zona más adelantada (UTC+14). Filtro grueso para la consulta; el día extra
    /// cubre el 29 de febrero (+12 meses cae en el 28 y restar meses no lo deshace).
    /// </summary>
    public static DateOnly LatestCandidateBase(DateTime nowUtc) =>
        DateOnly
            .FromDateTime(nowUtc.AddHours(14))
            .AddDays(WispReviewSchedule.DueSoonDays + 1)
            .AddMonths(-WispReviewSchedule.ReviewIntervalMonths);

    private static string ToContract(WispReviewReminderStage stage) =>
        stage switch
        {
            WispReviewReminderStage.DueSoon => OfficeWispReviewStages.DueSoon,
            WispReviewReminderStage.Overdue => OfficeWispReviewStages.Overdue,
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown WISP reminder stage."),
        };
}
