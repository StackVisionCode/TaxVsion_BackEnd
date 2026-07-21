using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Customer.Application.Abstractions;

namespace TaxVision.Customer.Application.Employees.Consumers;

/// <summary>
/// Alimenta la proyeccion local TenantEmployeeDirectoryEntry desde el ciclo de vida de
/// usuario de Auth. Cierra el gap encontrado en la auditoria del track de chat tipado:
/// AssignPreparerHandler necesita saber si un UserId es realmente un empleado activo
/// del tenant antes de asignarlo como preparador de un Customer.
/// </summary>
public static class AuthUserDirectoryConsumer
{
    public static async Task Handle(
        UserRegisteredIntegrationEvent msg,
        ITenantEmployeeDirectoryRepository directory,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        await directory.UpsertAsync(msg.UserId, msg.TenantId, msg.ActorType, isActive: true, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    public static async Task Handle(
        UserDeactivatedIntegrationEvent msg,
        ITenantEmployeeDirectoryRepository directory,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        await directory.MarkInactiveAsync(msg.UserId, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    public static async Task Handle(
        UserReactivatedIntegrationEvent msg,
        ITenantEmployeeDirectoryRepository directory,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        await directory.MarkActiveAsync(msg.UserId, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
