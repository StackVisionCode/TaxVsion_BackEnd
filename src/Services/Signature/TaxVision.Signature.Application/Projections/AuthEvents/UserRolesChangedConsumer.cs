using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Application.Projections.AuthEvents;

/// <summary>
/// Consumer del <c>UserRolesChangedIntegrationEvent</c> de Auth. Actualiza (o crea) la
/// proyección local <c>UserPermissionsProjection</c>. Idempotente por
/// <c>PermissionsVersion</c> monotónica: eventos fuera de orden se aplican solo si
/// traen versión más nueva.
/// </summary>
public static class UserRolesChangedConsumer
{
    public static async Task Handle(
        UserRolesChangedIntegrationEvent evt,
        IUserPermissionsProjectionRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<UserPermissionsProjection> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;
        using (correlation.Push(correlationId))
        {
            var existing = await repository.GetAsync(evt.TenantId, evt.UserId, ct);
            if (existing is null)
            {
                var projection = UserPermissionsProjection.ForNewUser(
                    evt.TenantId,
                    evt.UserId,
                    evt.PermissionsVersion,
                    evt.RoleNames
                );
                await repository.AddAsync(projection, ct);
                logger.LogInformation(
                    "UserPermissionsProjection created for {UserId} version {Version}.",
                    evt.UserId,
                    evt.PermissionsVersion
                );
            }
            else
            {
                existing.ApplyIfNewer(evt.PermissionsVersion, evt.RoleNames);
            }
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
