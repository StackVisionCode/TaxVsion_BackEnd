using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Customer.Application.Abstractions;

namespace TaxVision.Customer.Application.Consumers;

/// <summary>
/// Mantiene <c>IsActive</c> de la proyección de permisos en sync con el ciclo de vida del usuario en
/// Auth: desactivar/retirar → fail-closed (la lectura de permisos filtra por <c>IsActive</c>);
/// reactivar → vuelve a autorizar. El evento no trae permisos, solo mueve el flag (los permisos llegan
/// por UserRolesChanged/RolePermissionsChanged, y el reconciliador de Auth solo republica para usuarios
/// activos, así que no revive esta desactivación). Distinta de TenantEmployeeDirectoryEntry (elegibilidad
/// de preparador): esta es la de AUTORIZACIÓN. Idempotente; no-op si la proyección aún no existe.
/// </summary>
public static class UserLifecyclePermissionsProjectionConsumer
{
    public static Task Handle(
        UserDeactivatedIntegrationEvent evt,
        IUserPermissionsProjectionRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        ApplyAsync(
            evt.TenantId,
            evt.UserId,
            active: false,
            Correlation(evt.CorrelationId, evt.EventId),
            repository,
            unitOfWork,
            correlation,
            ct
        );

    public static Task Handle(
        UserOffboardedIntegrationEvent evt,
        IUserPermissionsProjectionRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        ApplyAsync(
            evt.TenantId,
            evt.UserId,
            active: false,
            Correlation(evt.CorrelationId, evt.EventId),
            repository,
            unitOfWork,
            correlation,
            ct
        );

    public static Task Handle(
        UserReactivatedIntegrationEvent evt,
        IUserPermissionsProjectionRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        ApplyAsync(
            evt.TenantId,
            evt.UserId,
            active: true,
            Correlation(evt.CorrelationId, evt.EventId),
            repository,
            unitOfWork,
            correlation,
            ct
        );

    private static string Correlation(string? correlationId, Guid eventId) =>
        string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId;

    private static async Task ApplyAsync(
        Guid tenantId,
        Guid userId,
        bool active,
        string correlationId,
        IUserPermissionsProjectionRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(correlationId))
        {
            var existing = await repository.GetAsync(tenantId, userId, ct);
            if (existing is null)
                return;

            if (active)
                existing.MarkActive();
            else
                existing.MarkInactive();

            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
