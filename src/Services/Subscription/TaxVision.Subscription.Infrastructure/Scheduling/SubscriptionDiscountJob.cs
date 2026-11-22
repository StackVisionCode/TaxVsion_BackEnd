using BuildingBlocks.Jobs;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.TenantTimeZones;
using BuildingBlocks.TimeZones;
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
/// avisa del fin de uno de N meses desde 7 días antes, a partir de las 9:00 de la oficina. El aviso deja su marca en
/// el descuento en la misma unidad con outbox: sale una sola vez aunque el job corra dos veces o se salte un día.
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

        // El job es el origen de la traza: un id por pasada.
        var correlationId = Guid.NewGuid().ToString("N");
        var due = await DueNoticesAsync(services, discounts, nowUtc, correlationId, ct);
        var sent = await RunUnitsAsync(
            due,
            (candidate, unit, token) => SendNoticeAsync(unit, candidate, nowUtc, correlationId, token),
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

    // Relee el descuento dentro de la unidad: la marca y el aviso se confirman juntos o nada.
    private static async Task<bool> SendNoticeAsync(
        JobUnitContext<SubscriptionDbContext> unit,
        DueNotice candidate,
        DateTime nowUtc,
        string correlationId,
        CancellationToken ct
    )
    {
        unit.UseTenant(candidate.TenantId);
        var subscription = await unit
            .Services.GetRequiredService<ISubscriptionRepository>()
            .GetWithFullHistoryAsync(candidate.TenantId, ct);
        if (
            subscription is null
            || SubscriptionDiscountEndingNotice.Take(
                subscription,
                candidate.DiscountId,
                nowUtc,
                candidate.Zone,
                correlationId
            )
                is not { } notice
        )
            return false;

        await unit.PublishAsync(notice);
        return true;
    }

    // Los de N meses sin aviso que terminan en los próximos días; la regla decide si ya toca avisar.
    private static async Task<List<DueNotice>> DueNoticesAsync(
        IServiceProvider services,
        DbSet<SubscriptionDiscount> discounts,
        DateTime nowUtc,
        string correlationId,
        CancellationToken ct
    )
    {
        var windowEndUtc = nowUtc.AddDays(SubscriptionDiscountEndingNotice.DaysBefore + 1);
        var ending = await discounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(discount =>
                discount.EndedAtUtc == null
                && discount.EndingNoticeSentAtUtc == null
                && discount.Duration == DiscountDuration.Months
                && discount.EndsAtUtc > nowUtc
                && discount.EndsAtUtc <= windowEndUtc
            )
            .Take(BatchSize)
            .ToListAsync(ct);

        var zones = services.GetRequiredService<ITenantTimeZoneResolver>();
        var due = new List<DueNotice>();
        foreach (var discount in ending)
        {
            var zone = await zones.ResolveAsync(discount.TenantId, ct);
            if (SubscriptionDiscountEndingNotice.For(discount, nowUtc, zone, correlationId) is not null)
                due.Add(new DueNotice(discount.TenantId, discount.Id, zone));
        }

        return due;
    }

    private sealed record DueNotice(Guid TenantId, Guid DiscountId, ResolvedTimeZone Zone);
}
