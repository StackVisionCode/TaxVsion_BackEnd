using BuildingBlocks.Jobs;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Infrastructure.Persistence;

namespace TaxVision.PaymentApp.Infrastructure.Scheduling;

/// <summary>
/// Avisa con 30 días de anticipación cuando un método de pago guardado está por vencer —
/// evita que una renovación automática falle en silencio por tarjeta expirada. Un solo aviso
/// por método (<see cref="Domain.ProviderCustomers.TenantSavedPaymentMethod.ExpiryNoticeSentAtUtc"/>),
/// nunca se repite en corridas siguientes. Una unidad transaccional por cliente del proveedor: la marca y
/// el aviso se confirman juntos, y un conflicto en un cliente no arrastra a los demás.
/// </summary>
public sealed class ExpiringPaymentMethodsJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    ILogger<ExpiringPaymentMethodsJob> logger
) : PeriodicPaymentAppJob(scopeFactory, lockFactory, logger, TimeSpan.FromHours(24), TimeSpan.FromMinutes(30))
{
    private const int BatchSize = 200;
    private static readonly TimeSpan NoticeWindow = TimeSpan.FromDays(30);

    protected override string JobName => "expiring-payment-methods";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var customers = services.GetRequiredService<ITenantProviderCustomerRepository>();
        var logger = services.GetRequiredService<ILogger<ExpiringPaymentMethodsJob>>();

        var cutoffUtc = DateTime.UtcNow + NoticeWindow;
        var affected = await customers.GetWithMethodsExpiringBeforeAsync(cutoffUtc, BatchSize, ct);
        if (affected.Count == 0)
            return;

        // El job es el origen de la traza: un id por pasada, para seguir junto todo lo que publique.
        var summary = await NotifyAsync(
            [.. affected.Select(customer => (customer.Id, customer.TenantId))],
            cutoffUtc,
            Guid.NewGuid().ToString("N"),
            ct
        );
        if (summary.Committed > 0)
            logger.LogInformation(
                "ExpiringPaymentMethodsJob notified {Count} provider customer(s).",
                summary.Committed
            );
    }

    internal Task<JobBatchSummary> NotifyAsync(
        IReadOnlyList<(Guid CustomerId, Guid TenantId)> customers,
        DateTime cutoffUtc,
        string correlationId,
        CancellationToken ct
    ) =>
        RunUnitsAsync(
            customers,
            (item, unit, token) =>
                NotifyOneAsync(unit, item.CustomerId, item.TenantId, cutoffUtc, correlationId, token),
            ct
        );

    private static async Task<bool> NotifyOneAsync(
        JobUnitContext<PaymentAppDbContext> unit,
        Guid customerId,
        Guid tenantId,
        DateTime cutoffUtc,
        string correlationId,
        CancellationToken ct
    )
    {
        var customer = await unit
            .Services.GetRequiredService<ITenantProviderCustomerRepository>()
            .GetByIdAsync(customerId, tenantId, ct);
        if (customer is null)
            return false;

        var notices = 0;
        foreach (var method in customer.SavedMethods)
        {
            if (method.IsDetached || method.ExpiryNoticeSentAtUtc is not null || !method.ExpiresBefore(cutoffUtc))
                continue;

            await unit.PublishAsync(
                new SaaSPaymentMethodExpiringSoonIntegrationEvent
                {
                    TenantId = customer.TenantId,
                    CorrelationId = correlationId,
                    TenantProviderCustomerId = customer.Id,
                    PaymentMethodId = method.Id,
                    Brand = method.Brand,
                    Last4 = method.Last4,
                    ExpMonth = method.ExpMonth,
                    ExpYear = method.ExpYear,
                    IsDefault = method.IsDefault,
                }
            );
            method.MarkExpiryNoticeSent(DateTime.UtcNow);
            notices++;
        }

        return notices > 0;
    }
}
