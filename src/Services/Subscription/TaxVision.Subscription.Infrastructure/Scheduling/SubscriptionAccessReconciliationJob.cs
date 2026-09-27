using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Domain.Subscriptions;
using Wolverine;

namespace TaxVision.Subscription.Infrastructure.Scheduling;

/// <summary>
/// A6 — anti-entropía del corte de acceso por facturación.
///
/// Los demás servicios no guardan el estado de la suscripción: guardan su CONSECUENCIA (Auth pone
/// <c>Tenant.BillingAccessBlocked</c> al consumir <c>TenantSubscriptionStatusChanged</c>). Un evento
/// perdido —consumidor caído, cola purgada, base restaurada, o el consumidor desplegado DESPUÉS de la
/// transición— deja esa consecuencia desincronizada para siempre, porque solo se recalcula cuando el
/// estado vuelve a cambiar, y un tenant vencido ya no cambia de estado.
///
/// Medido el 2026-09-27 en la base local: Subscription tenía 1 <c>Expired</c> y 8 <c>PastDue</c>,
/// mientras Auth tenía <c>BillingAccessBlocked = 0</c> en los 18 tenants. Con el gate de módulo en
/// enforce eso da la peor combinación posible: el tenant vencido recibiría 403 en todo mientras
/// <c>/auth/me/access</c> le responde <c>state: "active"</c> — la pantalla diciendo que está bien y el
/// backend denegando.
///
/// Este job vuelve a anunciar el estado ACTUAL para que cada servicio converja. No decide nada nuevo
/// ni duplica la regla: reusa el mismo evento y los mismos consumidores, que ya son idempotentes.
///
/// <para>
/// Solo re-anuncia los estados que DETERMINAN el corte: los que bloquean (<c>Suspended</c>,
/// <c>Expired</c>) y <c>Active</c>, que lo levanta. Los intermedios (PastDue, GracePeriod, Trialing,
/// Cancelled) no tocan el flag — re-anunciarlos sería ruido sin efecto. Las dos direcciones importan:
/// un tenant vencido sin bloquear sigue entrando, y uno al día marcado como bloqueado no puede entrar
/// aunque pagó.
/// </para>
///
/// El primer tick de <see cref="PeriodicSubscriptionJob"/> es inmediato, así que además de correr a
/// diario reconcilia al arrancar, que es cuando más falta hace tras un despliegue.
/// </summary>
public sealed class SubscriptionAccessReconciliationJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    ILogger<SubscriptionAccessReconciliationJob> logger
) : PeriodicSubscriptionJob(scopeFactory, lockFactory, logger, TimeSpan.FromHours(24), TimeSpan.FromMinutes(30))
{
    private const int BatchSize = 200;

    /// <summary>Los estados de los que depende <c>BillingAccessBlocked</c>, y solo esos.</summary>
    private static readonly SubscriptionStatus[] AccessDecidingStatuses =
    [
        SubscriptionStatus.Suspended,
        SubscriptionStatus.Expired,
        SubscriptionStatus.Active,
    ];

    protected override string JobName => "subscription-access-reconciliation";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var subscriptions = services.GetRequiredService<ISubscriptionRepository>();
        var bus = services.GetRequiredService<IMessageBus>();
        var log = services.GetRequiredService<ILogger<SubscriptionAccessReconciliationJob>>();

        var announced = 0;
        var afterTenantId = Guid.Empty;

        while (!ct.IsCancellationRequested)
        {
            var batch = await subscriptions.GetByStatusesAsync(AccessDecidingStatuses, afterTenantId, BatchSize, ct);
            if (batch.Count == 0)
                break;

            foreach (var subscription in batch)
            {
                // `previousStatus` = el actual: no hubo transición, se está re-anunciando lo que ya
                // es. Los consumidores miran `Status`, no la diferencia.
                await bus.PublishStatusChangedAsync(
                    subscription,
                    subscription.Status,
                    SubscriptionChangeReason.Reconciliation
                );
                announced++;
            }

            afterTenantId = batch[^1].TenantId;
            if (batch.Count < BatchSize)
                break;
        }

        if (announced > 0)
            log.LogInformation(
                "SubscriptionAccessReconciliationJob re-announced the current status of {Count} subscription(s) "
                    + "so downstream services converge.",
                announced
            );
    }
}
