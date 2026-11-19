using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Mobile.Application.Projections.Abstractions;
using TaxVision.Mobile.Domain.Projections;

namespace TaxVision.Mobile.Application.Projections.Consumers;

public static class UserLifecyclePermissionsProjectionConsumer
{
    public static Task Handle(
        UserDeactivatedIntegrationEvent evt,
        IMobileProjectionRepository projections,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        SetUserActive(
            evt.TenantId,
            evt.UserId,
            false,
            evt.OccurredOn,
            string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId,
            projections,
            unitOfWork,
            correlation,
            ct
        );

    public static Task Handle(
        UserOffboardedIntegrationEvent evt,
        IMobileProjectionRepository projections,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        SetUserActive(
            evt.TenantId,
            evt.UserId,
            false,
            evt.OccurredOn,
            string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId,
            projections,
            unitOfWork,
            correlation,
            ct
        );

    public static Task Handle(
        UserReactivatedIntegrationEvent evt,
        IMobileProjectionRepository projections,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        SetUserActive(
            evt.TenantId,
            evt.UserId,
            true,
            evt.OccurredOn,
            string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId,
            projections,
            unitOfWork,
            correlation,
            ct
        );

    private static async Task SetUserActive(
        Guid tenantId,
        Guid userId,
        bool active,
        DateTime observedAtUtc,
        string correlationId,
        IMobileProjectionRepository projections,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(correlationId))
        {
            var row = await projections.FindUserPermissionsAsync(tenantId, userId, ct);
            if (row is null)
            {
                // Reactivado sin fila: la primera petición trae la foto completa de Auth.
                if (active)
                    return;
                row = UserPermissionsProjection.CreateInactive(tenantId, userId, observedAtUtc);
                projections.AddUserPermissions(row);
            }
            row.SetActive(active, observedAtUtc);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
