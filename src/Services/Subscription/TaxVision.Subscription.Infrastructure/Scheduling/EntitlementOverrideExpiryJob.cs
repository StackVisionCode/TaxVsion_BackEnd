using BuildingBlocks.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Infrastructure.Persistence;

namespace TaxVision.Subscription.Infrastructure.Scheduling;

/// <summary>
/// Plan a la medida: cada hora busca las excepciones que vencieron y recalcula esas oficinas para que vuelvan
/// a su plan. El cálculo ya ignora una excepción vencida; este job solo hace que el cambio llegue a los demás
/// servicios sin esperar al próximo recálculo.
/// <para>
/// Una oficina por unidad con outbox (BB-3): la marca de "ya recalculada" y el recálculo encolado se confirman
/// juntos o nada. Si el job corre dos veces o se cae a mitad, ninguna oficina se recalcula de más ni se pierde.
/// </para>
/// </summary>
public sealed class EntitlementOverrideExpiryJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    ILogger<EntitlementOverrideExpiryJob> logger
) : PeriodicSubscriptionJob(scopeFactory, lockFactory, logger, Interval, TimeSpan.FromMinutes(10))
{
    // Una excepción puede quedar vigente hasta una hora de más; a cambio, el job casi no pesa.
    internal static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private const int BatchSize = 500;

    protected override string JobName => "entitlement-override-expiry";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        var pending = await services
            .GetRequiredService<ITenantEntitlementOverrideRepository>()
            .ListExpiryPendingAsync(nowUtc, BatchSize, ct);
        var offices = pending
            .GroupBy(item => item.TenantId)
            .Select(group => (TenantId: group.Key, Keys: group.Select(item => item.Key).ToList()))
            .ToList();

        var summary = await RunUnitsAsync(
            offices,
            (office, unit, token) =>
            {
                unit.UseTenant(office.TenantId);
                return ApplyExpiryAsync(
                    unit.Services.GetRequiredService<ITenantEntitlementOverrideRepository>(),
                    command => unit.PublishAsync(command),
                    office.TenantId,
                    office.Keys,
                    nowUtc,
                    token
                );
            },
            ct
        );

        if (summary.Committed > 0)
            Logger.LogInformation(
                "EntitlementOverrideExpiryJob sent {Count} office(s) back to their plan values.",
                summary.Committed
            );
    }

    /// <summary>
    /// Relee cada excepción dentro de la unidad: si el admin la extendió entre la lista y ahora, sigue vigente y
    /// no se toca. Devuelve si hubo algo que confirmar.
    /// </summary>
    internal static async Task<bool> ApplyExpiryAsync(
        ITenantEntitlementOverrideRepository overrides,
        Func<RecalculateEntitlementsCommand, ValueTask> publish,
        Guid tenantId,
        IReadOnlyList<string> keys,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var marked = false;
        foreach (var key in keys)
        {
            var item = await overrides.GetForUpdateAsync(tenantId, key, ct);
            if (item is null || item.ExpiryAppliedAtUtc is not null)
                continue;

            marked |= item.MarkExpiryApplied(nowUtc).IsSuccess;
        }

        if (marked)
            await publish(new RecalculateEntitlementsCommand(tenantId));
        return marked;
    }
}
