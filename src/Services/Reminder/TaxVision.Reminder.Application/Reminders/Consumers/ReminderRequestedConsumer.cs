using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Reminder.Application.Reminders.Abstractions;
using TaxVision.Reminder.Application.Reminders.Commands;
using TaxVision.Reminder.Domain.Reminders;

namespace TaxVision.Reminder.Application.Reminders.Consumers;

/// <summary>
/// <c>reminder.requested.v1</c> — la vía por bus para crear un recordatorio, equivalente al
/// <c>POST /reminders</c>.
///
/// <para>
/// <b>Delega en <see cref="CreateReminderHandler"/>, no reimplementa el alta.</b> Esa es toda la
/// razón de que la idempotencia por <c>RequestKey</c> valga también acá: si el consumer construyera
/// su propio aggregate, un reintento del bus —que es rutina, no excepción— crearía un duplicado, y
/// el bug se vería como «me llegó el mismo aviso dos veces» sin ningún error en los logs.
/// </para>
/// </summary>
public static class ReminderRequestedConsumer
{
    public static async Task Handle(
        ReminderRequestedIntegrationEvent evt,
        IReminderRepository reminders,
        IReminderScheduler scheduler,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        IReminderMetrics metrics,
        ILogger<ReminderAggregate> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(ReminderInboundEvent.CorrelationOf(evt)))
        {
            if (!ReminderInboundEvent.TryParseCategory(evt.Category, out var category))
            {
                ReminderInboundEvent.LogUnknownCategory(logger, evt.Category, evt.EventId, "reminder.requested.v1");
                return;
            }

            var result = await CreateReminderHandler.Handle(
                new CreateReminderCommand(
                    evt.TenantId,
                    evt.UserId,
                    evt.Title,
                    evt.Body,
                    category,
                    evt.TargetId,
                    evt.FireAtUtc,
                    evt.AnchorAtUtc,
                    evt.LeadMinutes,
                    evt.TimeZoneId,
                    evt.RequestKey
                ),
                reminders,
                scheduler,
                unitOfWork,
                metrics,
                logger,
                ct
            );

            if (result.IsFailure)
            {
                // Un evento con datos inválidos (hora en el pasado, categoría sin objetivo, request
                // key vacía) no mejora reintentándolo: el publicador tiene que corregirlo y volver a
                // publicar. Reintentarlo sería un bucle infinito contra la DLQ.
                logger.LogWarning(
                    "reminder.requested.v1 {EventId} of tenant {TenantId} was discarded: {ErrorCode}.",
                    evt.EventId,
                    evt.TenantId,
                    result.Error.Code
                );
            }
        }
    }
}
