using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Domain.Audit;
using Wolverine;

namespace TaxVision.Auth.Application.Customers.IntegrationEvents;

/// <summary>D-POR-1: archivar el cliente desactiva su cuenta de portal y corta sus sesiones al instante.</summary>
public static class CustomerArchivedConsumer
{
    public const string CutoffReason = "customer_archived";

    public static async Task Handle(
        CustomerArchivedIntegrationEvent evt,
        IUserRepository users,
        SessionAccessCutoff accessCutoff,
        IAuthAuditWriter audit,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        using var _ = correlation.Push(string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId);

        var user = await users.GetPortalUserByCustomerAsync(evt.TenantId, evt.CustomerId, ct);
        if (user is null)
            return;

        var wasActive = user.IsActive;
        var applied = user.ApplyCustomerArchived(evt.OccurredOn, DateTime.UtcNow);
        if (applied.IsFailure || user.IsActive)
            return;

        // Mismo corte que la baja y el retiro: denylist, anuncio y revocación. Repetirlo no cambia nada.
        await accessCutoff.ForUserAsync(user, CutoffReason, ct);
        if (wasActive)
        {
            await audit.AddAsync(
                AuthAuditLog.Record(
                    user.TenantId,
                    evt.ArchivedByUserId,
                    AuthAuditAction.UserDeactivated,
                    true,
                    null,
                    null,
                    correlation.CorrelationId,
                    targetType: "User",
                    targetId: user.Id,
                    detailsJson: $$"""{"reason":"{{CutoffReason}}","customerId":"{{evt.CustomerId}}"}"""
                ),
                ct
            );
        }

        await unitOfWork.SaveChangesAsync(ct);
        if (wasActive)
        {
            await bus.PublishAsync(
                new UserDeactivatedIntegrationEvent
                {
                    TenantId = user.TenantId,
                    UserId = user.Id,
                    Email = user.Email,
                    ActorType = user.ActorType.ToString(),
                    CorrelationId = correlation.CorrelationId,
                }
            );
        }
    }
}
