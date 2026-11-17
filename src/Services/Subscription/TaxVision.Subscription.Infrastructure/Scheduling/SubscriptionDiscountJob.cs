using BuildingBlocks.Jobs;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.TenantTimeZones;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Subscriptions;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Infrastructure.Persistence;

namespace TaxVision.Subscription.Infrastructure.Scheduling;

/// <summary>
/// Cada hora: cierra los descuentos cuya ventana ya terminó (cobrar bien no depende de esto, lo usa la consulta) y
/// avisa 7 días antes del fin de uno de N meses, a las 9:00 de la oficina; esa franja cae una vez por día, así que
/// cada aviso sale una sola vez sin tabla de log. Una unidad con outbox por oficina y por aviso.
/// </summary>
public sealed class SubscriptionDiscountJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    ILogger<SubscriptionDiscountJob> logger
) : PeriodicSubscriptionJob(scopeFactory, lockFactory, logger, TimeSpan.FromHours(1), TimeSpan.FromMinutes(7))
{
    private const int BatchSize = 500;

    protected override string JobName => "subscription-discount";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var discounts = services.GetRequiredService<SubscriptionDbContext>().Set<SubscriptionDiscount>();
        var nowUtc = DateTime.UtcNow;

        var expiredTenants = await discounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(discount =>
                discount.EndedAtUtc == null && discount.EndsAtUtc != null && discount.EndsAtUtc <= nowUtc
            )
            .Select(discount => discount.TenantId)
            .Distinct()
            .Take(BatchSize)
            .ToListAsync(ct);
        var closed = await RunUnitsAsync(
            expiredTenants,
            (tenantId, unit, token) => CloseExpiredAsync(unit, tenantId, nowUtc, token),
            ct
        );

        var notices = await EndingNoticesAsync(services, discounts, nowUtc, ct);
        var sent = await RunUnitsAsync(
            notices,
            async (notice, unit, _) =>
            {
                await unit.PublishAsync(notice);
                return true;
            },
            ct
        );

        if (closed.Committed + sent.Committed > 0)
            Logger.LogInformation(
                "SubscriptionDiscountJob closed discounts of {Closed} office(s) and sent {Sent} ending notice(s).",
                closed.Committed,
                sent.Committed
            );
    }

    private static async Task<bool> CloseExpiredAsync(
        JobUnitContext<SubscriptionDbContext> unit,
        Guid tenantId,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        unit.UseTenant(tenantId);
        var subscription = await unit
            .Services.GetRequiredService<ISubscriptionRepository>()
            .GetWithFullHistoryAsync(tenantId, ct);
        return subscription?.CloseExpiredDiscounts(nowUtc) ?? false;
    }

    // Los de N meses que terminan en los próximos días; la regla decide si hoy, a esta hora, toca avisar.
    private static async Task<List<SubscriptionDiscountEndingIntegrationEvent>> EndingNoticesAsync(
        IServiceProvider services,
        DbSet<SubscriptionDiscount> discounts,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var windowEndUtc = nowUtc.AddDays(SubscriptionDiscountEndingNotice.DaysBefore + 1);
        var ending = await discounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(discount =>
                discount.EndedAtUtc == null
                && discount.Duration == DiscountDuration.Months
                && discount.EndsAtUtc > nowUtc
                && discount.EndsAtUtc <= windowEndUtc
            )
            .Take(BatchSize)
            .ToListAsync(ct);

        var zones = services.GetRequiredService<ITenantTimeZoneResolver>();
        // El job es el origen de la traza: un id por pasada.
        var correlationId = Guid.NewGuid().ToString("N");
        var notices = new List<SubscriptionDiscountEndingIntegrationEvent>();
        foreach (var discount in ending)
        {
            var zone = await zones.ResolveAsync(discount.TenantId, ct);
            if (SubscriptionDiscountEndingNotice.For(discount, nowUtc, zone, correlationId) is { } notice)
                notices.Add(notice);
        }

        return notices;
    }
}
