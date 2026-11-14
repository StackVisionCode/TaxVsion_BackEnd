using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.FinancialInsights.Application.Projections.Abstractions;
using TaxVision.FinancialInsights.Domain.Projections;

namespace TaxVision.FinancialInsights.Application.Projections.Consumers;

/// <summary>
/// Ciclo de vida del usuario sobre la proyección de permisos. Es el paso de Financial Insights en el
/// retiro de un empleado (OffboardingParticipants): un usuario retirado o desactivado deja de pasar
/// el filtro de permisos; los lotes que importó se conservan como procedencia.
/// </summary>
public static class UserLifecyclePermissionsProjectionConsumer
{
    public static Task Handle(
        UserDeactivatedIntegrationEvent evt,
        IFinancialProjectionRepository projections,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) => SetUserActive(evt.TenantId, evt.UserId, false, evt.OccurredOn, ProjectionConsumer.CorrelationOf(evt), projections, unitOfWork, correlation, ct);

    public static Task Handle(
        UserOffboardedIntegrationEvent evt,
        IFinancialProjectionRepository projections,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) => SetUserActive(evt.TenantId, evt.UserId, false, evt.OccurredOn, ProjectionConsumer.CorrelationOf(evt), projections, unitOfWork, correlation, ct);

    public static Task Handle(
        UserReactivatedIntegrationEvent evt,
        IFinancialProjectionRepository projections,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) => SetUserActive(evt.TenantId, evt.UserId, true, evt.OccurredOn, ProjectionConsumer.CorrelationOf(evt), projections, unitOfWork, correlation, ct);

    private static async Task SetUserActive(
        Guid tenantId,
        Guid userId,
        bool active,
        DateTime observedAtUtc,
        string correlationId,
        IFinancialProjectionRepository projections,
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
