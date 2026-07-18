using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using Wolverine;

namespace TaxVision.PaymentApp.Infrastructure.Scheduling;

/// <summary>
/// Avisa con 30 días de anticipación cuando un método de pago guardado está por vencer —
/// evita que una renovación automática falle en silencio por tarjeta expirada. Un solo aviso
/// por método (<see cref="Domain.ProviderCustomers.TenantSavedPaymentMethod.ExpiryNoticeSentAtUtc"/>),
/// nunca se repite en corridas siguientes.
/// </summary>
public sealed class ExpiringPaymentMethodsJob(
    IServiceScopeFactory scopeFactory, IDistributedLockFactory lockFactory, ILogger<ExpiringPaymentMethodsJob> logger)
    : PeriodicPaymentAppJob(scopeFactory, lockFactory, logger, TimeSpan.FromHours(24), TimeSpan.FromMinutes(30))
{
    private const int BatchSize = 200;
    private static readonly TimeSpan NoticeWindow = TimeSpan.FromDays(30);

    protected override string JobName => "expiring-payment-methods";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var customers = services.GetRequiredService<ITenantProviderCustomerRepository>();
        var bus = services.GetRequiredService<IMessageBus>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var logger = services.GetRequiredService<ILogger<ExpiringPaymentMethodsJob>>();

        var cutoffUtc = DateTime.UtcNow + NoticeWindow;
        var affected = await customers.GetWithMethodsExpiringBeforeAsync(cutoffUtc, BatchSize, ct);

        var noticeCount = 0;
        foreach (var customer in affected)
        {
            foreach (var method in customer.SavedMethods)
            {
                if (method.IsDetached || method.ExpiryNoticeSentAtUtc is not null || !method.ExpiresBefore(cutoffUtc))
                    continue;

                await bus.PublishAsync(new SaaSPaymentMethodExpiringSoonIntegrationEvent
                {
                    TenantId = customer.TenantId,
                    TenantProviderCustomerId = customer.Id,
                    PaymentMethodId = method.Id,
                    Brand = method.Brand,
                    Last4 = method.Last4,
                    ExpMonth = method.ExpMonth,
                    ExpYear = method.ExpYear,
                    IsDefault = method.IsDefault,
                });

                method.MarkExpiryNoticeSent(DateTime.UtcNow);
                noticeCount++;
            }
        }

        if (noticeCount > 0)
        {
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation("ExpiringPaymentMethodsJob sent {Count} expiry notice(s).", noticeCount);
        }
    }
}
