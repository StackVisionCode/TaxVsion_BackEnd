using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using TaxVision.PaymentClient.Application.Abstractions;

namespace TaxVision.PaymentClient.Application.Tenants.IntegrationEvents;

/// <summary>Alimenta la proyección local <see cref="Domain.Tenants.Tenant"/> — PaymentClient
/// nunca llama a Auth/Tenant en el hot path de un cobro, misma estrategia que PaymentApp.</summary>
public static class TenantCreatedConsumer
{
    public static async Task Handle(
        TenantCreatedIntegrationEvent evt,
        ITenantRegistry tenants,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var kind = Enum.TryParse<TenantKind>(evt.Kind, true, out var parsedKind) ? parsedKind : TenantKind.Customer;
            var nowUtc = DateTime.UtcNow;

            await tenants.UpsertCreatedAsync(evt.NewTenantId, evt.Name, evt.SubDomain, kind, evt.DefaultTimeZoneId, nowUtc, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
