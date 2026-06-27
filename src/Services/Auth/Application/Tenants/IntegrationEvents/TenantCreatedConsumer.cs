using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using BuildingBlocks.Common;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Application.Tenants.IntegrationEvents;

public static class TenantCreatedConsumer
{
    public static async Task Handle(
        TenantCreatedIntegrationEvent evt,
        ITenantRegistry tenants,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            await tenants.UpsertCreatedAsync(
                evt.NewTenantId,
                evt.Name,
                evt.SubDomain,
                evt.AdminEmail,
                evt.AdminInvitationTokenHash,
                ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
