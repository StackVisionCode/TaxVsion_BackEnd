using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Notification.Application.Authorization.Abstractions;
using TaxVision.Notification.Application.Consumers;

namespace TaxVision.Notification.Application.Authorization.Consumers;

/// <summary>
/// Mantiene <c>IsActive</c> de la proyección de permisos de AUTORIZACIÓN en sync con el ciclo de vida del
/// usuario en Auth: suspender/retirar → fail-closed (<c>GetSnapshotAsync</c> filtra por <c>IsActive</c>);
/// reactivar → vuelve a autorizar. Retiro ⊇ suspensión. Idempotente; no-op si la proyección aún no existe.
/// Mismo patrón que el consumer homónimo de los demás servicios; la de audiencias tiene el suyo.
/// </summary>
public static class AuthzUserLifecyclePermissionsProjectionConsumer
{
    public static Task Handle(
        UserDeactivatedIntegrationEvent evt,
        IAuthzUserPermissionsProjectionRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        ApplyAsync(
            evt.TenantId,
            evt.UserId,
            false,
            Correlation.From(evt.CorrelationId, evt.EventId),
            repository,
            unitOfWork,
            correlation,
            ct
        );

    public static Task Handle(
        UserOffboardedIntegrationEvent evt,
        IAuthzUserPermissionsProjectionRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        ApplyAsync(
            evt.TenantId,
            evt.UserId,
            false,
            Correlation.From(evt.CorrelationId, evt.EventId),
            repository,
            unitOfWork,
            correlation,
            ct
        );

    public static Task Handle(
        UserReactivatedIntegrationEvent evt,
        IAuthzUserPermissionsProjectionRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        ApplyAsync(
            evt.TenantId,
            evt.UserId,
            true,
            Correlation.From(evt.CorrelationId, evt.EventId),
            repository,
            unitOfWork,
            correlation,
            ct
        );

    private static async Task ApplyAsync(
        Guid tenantId,
        Guid userId,
        bool active,
        string correlationId,
        IAuthzUserPermissionsProjectionRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(correlationId))
        {
            var existing = await repository.GetAsync(tenantId, userId, ct);
            if (existing is null || existing.IsActive == active)
                return;

            if (active)
                existing.MarkActive();
            else
                existing.MarkInactive();

            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
