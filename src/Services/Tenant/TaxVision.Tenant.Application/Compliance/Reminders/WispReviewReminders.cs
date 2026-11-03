using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Tenant.Application.Compliance.Abstractions;
using TaxVision.Tenant.Domain.Compliance;
using Wolverine;

namespace TaxVision.Tenant.Application.Compliance.Reminders;

/// <summary>
/// Un paso del job diario del WISP para una oficina: el agregado decide si hoy (en su zona horaria) toca
/// avisar y lo anota; se guarda y recién después se publica <see cref="OfficeWispReviewDueIntegrationEvent"/>.
/// Si el guardado falla (p. ej. otra instancia ya lo anotó: <c>ConflictException</c>) la excepción sube y no
/// se publica nada.
/// </summary>
public static class WispReviewReminders
{
    /// <summary>Lo avisado, o null si hoy no tocaba.</summary>
    public static async Task<WispReviewReminder?> SendIfDueAsync(
        WispReminderCandidate candidate,
        DateTime nowUtc,
        string correlationId,
        IOfficeComplianceRepository compliances,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
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

        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new OfficeWispReviewDueIntegrationEvent
            {
                TenantId = compliance.TenantId,
                OfficeTenantId = compliance.TenantId,
                DueOn = reminder.DueOn,
                Stage = ToContract(reminder.Stage),
                SecurityCoordinatorUserId = compliance.SecurityCoordinatorUserId,
                CorrelationId = correlationId,
            }
        );
        return reminder;
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
