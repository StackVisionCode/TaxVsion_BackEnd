using BuildingBlocks.Domain;

namespace TaxVision.Tasks.Domain.Tasks.Events;

/// <summary>La tarea volvió de la papelera: su recordatorio de vencimiento se pide otra vez.</summary>
public sealed record TaskRestoredDomainEvent(Guid TaskId, Guid TenantId, Guid RestoredByUserId, DateTime RestoredAtUtc)
    : IDomainEvent;
