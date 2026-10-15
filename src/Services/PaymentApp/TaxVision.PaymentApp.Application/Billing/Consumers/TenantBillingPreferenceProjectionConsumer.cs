using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Billing.Abstractions;
using TaxVision.PaymentApp.Domain.Billing;

namespace TaxVision.PaymentApp.Application.Billing.Consumers;

/// <summary>
/// Proyecta <see cref="TenantBillingPreferenceChangedIntegrationEvent"/> en la copia local — mismo
/// patrón que TenantPlanCodeProjectionConsumer. Idempotente: ApplyIfNewer descarta eventos fuera
/// de orden por RevisionTicks.
/// </summary>
public static class TenantBillingPreferenceProjectionConsumer
{
    public static async Task Handle(
        TenantBillingPreferenceChangedIntegrationEvent evt,
        ITenantBillingPreferenceProjectionRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantBillingPreferenceProjection> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var existing = await repository.GetAsync(evt.TenantId, ct);
            if (existing is null)
            {
                var created = TenantBillingPreferenceProjection.Create(
                    evt.TenantId,
                    evt.Mode,
                    evt.ProviderCode,
                    evt.HasActiveMandate,
                    evt.AgreedAmountCents,
                    evt.AgreedCurrency,
                    evt.AgreedCadence,
                    evt.RevisionTicks,
                    evt.ChangedAtUtc
                );
                await repository.AddAsync(created, ct);
            }
            else
            {
                existing.ApplyIfNewer(
                    evt.Mode,
                    evt.ProviderCode,
                    evt.HasActiveMandate,
                    evt.AgreedAmountCents,
                    evt.AgreedCurrency,
                    evt.AgreedCadence,
                    evt.RevisionTicks,
                    evt.ChangedAtUtc
                );
            }

            await unitOfWork.SaveChangesAsync(ct);
            logger.LogDebug(
                "Projected TenantBillingPreference for tenant {TenantId}: Mode={Mode} Provider={Provider} Mandate={Mandate}",
                evt.TenantId,
                evt.Mode,
                evt.ProviderCode ?? "-",
                evt.HasActiveMandate
            );
        }
    }
}
