using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Campaigns.Application.Scheduling.Abstractions;
using TaxVision.Campaigns.Domain.Scheduling;
using Wolverine;

namespace TaxVision.Campaigns.Application.Scheduling.Consumers;

/// <summary>
/// D-6: al retirar a un empleado, los agendados vivos que disparan en su nombre pasan al sucesor con la
/// visibilidad de clientes DEL SUCESOR. Sin sucesor elegible (activo en este tenant y con
/// <c>campaigns.send</c>; ante la duda, no) quedan pausados y se publica
/// <see cref="CampaignSchedulesPausedOnOffboardingIntegrationEvent"/> para avisar a los admins. Cada paso
/// deja constancia. No toca lo ya enviado (runs) ni la procedencia (<c>CreatedByUserId</c>). Idempotente:
/// lo traspasado deja de ser suyo y lo pausado queda anotado en la constancia.
/// </summary>
public static class CampaignSchedulesUserOffboardedConsumer
{
    public static async Task Handle(
        UserOffboardedIntegrationEvent evt,
        ICampaignScheduleRepository schedules,
        ScheduleOffboardingResolver resolver,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<CampaignSchedule> logger,
        CancellationToken ct
    )
    {
        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
        {
            var open = await schedules.ListOpenRunAsUserAsync(evt.TenantId, evt.UserId, ct);
            if (open.Count == 0)
                return;

            var nowUtc = DateTime.UtcNow;
            if (await resolver.ResolveEligibleSuccessorAsync(evt, ct) is { } successor)
            {
                await HandOverAsync(open, evt, successor, schedules, nowUtc, ct);
                await unitOfWork.SaveChangesAsync(ct);
                logger.LogInformation(
                    "Offboarding {UserId}: {Count} campaign schedule(s) handed over to {SuccessorId}.",
                    evt.UserId,
                    open.Count,
                    successor.UserId
                );
                return;
            }

            var paused = await PauseAsync(open, evt, schedules, nowUtc, ct);
            await unitOfWork.SaveChangesAsync(ct);
            // Guardar antes de publicar: un guardado que falla no deja salir el aviso.
            if (paused.Count > 0)
                await bus.PublishAsync(
                    await resolver.PausedNoticeAsync(evt, paused, nowUtc, correlation.CorrelationId, ct)
                );
            logger.LogInformation(
                "Offboarding {UserId}: no eligible successor; {Count} campaign schedule(s) paused.",
                evt.UserId,
                paused.Count
            );
        }
    }

    private static async Task HandOverAsync(
        IReadOnlyList<CampaignSchedule> open,
        UserOffboardedIntegrationEvent evt,
        EligibleScheduleSender successor,
        ICampaignScheduleRepository schedules,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        foreach (var schedule in open)
        {
            if (schedule.HandOverOnOffboarding(successor.UserId, successor.CanViewAllCustomers).IsFailure)
                continue;
            await schedules.AddHandoverAsync(
                CampaignScheduleHandover.Reassigned(schedule, evt.UserId, evt.OffboardedByUserId, evt.EventId, nowUtc),
                ct
            );
        }
    }

    // Lo ya pausado por este mismo retiro no se vuelve a anotar ni a avisar.
    private static async Task<IReadOnlyList<CampaignSchedule>> PauseAsync(
        IReadOnlyList<CampaignSchedule> open,
        UserOffboardedIntegrationEvent evt,
        ICampaignScheduleRepository schedules,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var alreadyPaused = await schedules.ListPausedOnOffboardingAsync(evt.TenantId, evt.UserId, ct);
        var paused = new List<CampaignSchedule>();
        foreach (var schedule in open)
        {
            if (alreadyPaused.Contains(schedule.Id) || schedule.PauseOnOffboarding().IsFailure)
                continue;
            await schedules.AddHandoverAsync(
                CampaignScheduleHandover.Paused(schedule, evt.UserId, evt.OffboardedByUserId, evt.EventId, nowUtc),
                ct
            );
            paused.Add(schedule);
        }
        return paused;
    }
}
