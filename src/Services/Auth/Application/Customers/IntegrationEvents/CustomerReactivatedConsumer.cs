using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Audit;
using Wolverine;

namespace TaxVision.Auth.Application.Customers.IntegrationEvents;

/// <summary>D-POR-1: restaurar el cliente le devuelve el portal, solo si lo había cortado el archivado.</summary>
public static class CustomerReactivatedConsumer
{
    public static async Task Handle(
        CustomerReactivatedIntegrationEvent evt,
        IUserRepository users,
        IAuthAuditWriter audit,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        using var _ = correlation.Push(
            string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
        );

        var user = await users.GetPortalUserByCustomerAsync(evt.TenantId, evt.CustomerId, ct);
        if (user is null)
            return;

        var wasActive = user.IsActive;
        var applied = user.ApplyCustomerRestored(evt.ReactivatedAtUtc, DateTime.UtcNow);
        if (applied.IsFailure)
            return;

        if (!wasActive && user.IsActive)
        {
            await audit.AddAsync(
                AuthAuditLog.Record(
                    user.TenantId,
                    evt.ReactivatedByUserId,
                    AuthAuditAction.UserReactivated,
                    true,
                    null,
                    null,
                    correlation.CorrelationId,
                    targetType: "User",
                    targetId: user.Id,
                    detailsJson: $$"""{"reason":"customer_restored","customerId":"{{evt.CustomerId}}"}"""
                ),
                ct
            );
        }

        // Guarda también la fecha aplicada, para descartar un archivado viejo que llegue después.
        await unitOfWork.SaveChangesAsync(ct);
        if (!wasActive && user.IsActive)
        {
            await bus.PublishAsync(
                new UserReactivatedIntegrationEvent
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
