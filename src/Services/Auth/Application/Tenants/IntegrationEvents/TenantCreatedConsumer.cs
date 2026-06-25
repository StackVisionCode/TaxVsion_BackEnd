using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Application.Tenants.IntegrationEvents;

public static class TenantCreatedConsumer
{
    public static async Task Handle(
        TenantCreatedIntegrationEvent evt,
        ITenantRegistry tenants,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        await tenants.UpsertCreatedAsync(evt.NewTenantId, evt.Name, evt.SubDomain, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
