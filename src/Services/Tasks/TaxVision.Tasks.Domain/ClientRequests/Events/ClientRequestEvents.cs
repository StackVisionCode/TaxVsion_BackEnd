using BuildingBlocks.Domain;

namespace TaxVision.Tasks.Domain.ClientRequests.Events;

/// <summary>
/// El cliente mandó algo. Avisa al preparador; **no** mueve la tarea fuera de
/// <c>WaitingOnClient</c>: «apareció un archivo» no es «mandó lo que le pedí», y ese falso positivo
/// termina en una declaración presentada incompleta.
/// </summary>
public sealed record ClientRequestSubmittedDomainEvent(
    Guid ClientRequestId,
    Guid TenantId,
    Guid CustomerId,
    Guid? TaskId,
    string Title,
    DateTime SubmittedAtUtc
) : IDomainEvent;

/// <summary>
/// El escaneo rechazó un documento del cliente. Lleva el motivo real para el preparador; el aviso
/// que sale hacia el cliente se queda en «vuelve a subirlo».
/// </summary>
public sealed record ClientRequestDocumentRejectedDomainEvent(
    Guid ClientRequestId,
    Guid TenantId,
    Guid CustomerId,
    Guid? TaskId,
    Guid DocumentId,
    Guid FileId,
    string DisplayName,
    string Reason,
    Guid RequestedByUserId,
    DateTime RejectedAtUtc
) : IDomainEvent;

/// <summary>El preparador cerró el pedido: aceptado, rechazado o cancelado.</summary>
public sealed record ClientRequestResolvedDomainEvent(
    Guid ClientRequestId,
    Guid TenantId,
    Guid CustomerId,
    Guid? TaskId,
    string Status,
    string? Note,
    Guid ResolvedByUserId,
    DateTime ResolvedAtUtc
) : IDomainEvent;
