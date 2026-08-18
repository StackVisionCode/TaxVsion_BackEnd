using BuildingBlocks.Domain;

namespace TaxVision.Tasks.Domain.Tasks.Events;

/// <summary>
/// Se movió el vencimiento: hay que reprogramar el recordatorio en Reminder.
/// <c>StatutoryChangeReason</c> sólo viene poblada al aflojar un estatutario.
/// </summary>
public sealed record TaskDueChangedDomainEvent(
    Guid TaskId,
    Guid TenantId,
    DateTime? PreviousDueAtUtc,
    DateTime? NewDueAtUtc,
    string? TimeZoneId,
    Guid ChangedByUserId,
    DateTime ChangedAtUtc,
    string? StatutoryChangeReason = null
) : IDomainEvent;
