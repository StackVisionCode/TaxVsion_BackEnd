using BuildingBlocks.Domain;

namespace TaxVision.Tasks.Domain.Tasks.Events;

/// <summary>La tarea salió de Waiting on client: su aviso de respuesta del cliente ya no tiene sentido.</summary>
public sealed record TaskLeftWaitingOnClientDomainEvent(Guid TaskId, Guid TenantId) : IDomainEvent;
