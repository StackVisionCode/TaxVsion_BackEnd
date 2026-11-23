using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.Entitlements;
using Wolverine;

namespace TaxVision.Subscription.Application.Entitlements.Overrides;

/// <summary>PlatformAdmin fija el valor de un límite para UNA oficina (plan a la medida). Exige motivo.</summary>
public sealed record SetTenantEntitlementOverrideCommand(
    Guid TenantId,
    string Key,
    string Value,
    string Reason,
    DateTime? ExpiresAtUtc,
    Guid ActorUserId
);

/// <summary>PlatformAdmin quita la excepción: la oficina vuelve al valor de su plan.</summary>
public sealed record RemoveTenantEntitlementOverrideCommand(Guid TenantId, string Key, string Reason, Guid ActorUserId);

/// <summary>Las excepciones de una oficina, vigentes y vencidas.</summary>
public sealed record GetTenantEntitlementOverridesQuery(Guid TenantId);

public sealed record TenantEntitlementOverrideView(
    string Key,
    string ValueType,
    string Value,
    string Reason,
    DateTime? ExpiresAtUtc,
    bool InForce,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    Guid UpdatedBy
)
{
    public static TenantEntitlementOverrideView From(TenantEntitlementOverride item, DateTime nowUtc) =>
        new(
            item.Key,
            item.ValueType.ToString(),
            item.Value,
            item.Reason,
            item.ExpiresAtUtc,
            item.IsInForceAt(nowUtc),
            item.CreatedAtUtc,
            item.UpdatedAtUtc,
            item.UpdatedBy
        );
}

/// <summary>
/// Da o cambia una excepción. Orden de pasos (el del libro): validar con el dominio → guardar la fila y su
/// auditoría en la misma transacción → recién después encolar el recálculo. El recálculo se PUBLICA (outbox) y
/// no se invoca dentro: así corre cuando la fila ya está confirmada y nunca calcula con un estado a medias.
/// </summary>
public static class SetTenantEntitlementOverrideHandler
{
    public static async Task<Result<TenantEntitlementOverrideView>> Handle(
        SetTenantEntitlementOverrideCommand command,
        ISubscriptionRepository subscriptions,
        IModuleRepository modules,
        ITenantEntitlementOverrideRepository overrides,
        IUnitOfWork unitOfWork,
        ISubscriptionAuditLogWriter audit,
        ICorrelationContext correlation,
        IMessageBus bus,
        ILogger<TenantEntitlementOverride> logger,
        CancellationToken ct
    )
    {
        if (await subscriptions.GetByTenantIdAsync(command.TenantId, ct) is null)
            return Result.Failure<TenantEntitlementOverrideView>(
                new Error("Subscription.NotFound", "Subscription does not exist.")
            );

        var resolved = OverridableEntitlementKeys.Resolve(command.Key, await modules.GetAllAsync(ct));
        if (resolved.IsFailure)
            return Result.Failure<TenantEntitlementOverrideView>(resolved.Error);

        var (key, valueType) = resolved.Value;
        var nowUtc = DateTime.UtcNow;
        var existing = await overrides.GetForUpdateAsync(command.TenantId, key.Value, ct);
        var before = existing is null ? null : TenantEntitlementOverrideView.From(existing, nowUtc);

        TenantEntitlementOverride item;
        if (existing is null)
        {
            var granted = TenantEntitlementOverride.Grant(
                command.TenantId,
                key,
                valueType,
                command.Value,
                command.Reason,
                command.ExpiresAtUtc,
                command.ActorUserId,
                nowUtc
            );
            if (granted.IsFailure)
                return Result.Failure<TenantEntitlementOverrideView>(granted.Error);

            item = granted.Value;
            await overrides.AddAsync(item, ct);
        }
        else
        {
            var changed = existing.Change(
                command.Value,
                command.Reason,
                command.ExpiresAtUtc,
                command.ActorUserId,
                nowUtc
            );
            if (changed.IsFailure)
                return Result.Failure<TenantEntitlementOverrideView>(changed.Error);

            item = existing;
        }

        var view = TenantEntitlementOverrideView.From(item, nowUtc);
        await AuditEntryFactory.AppendAsync(
            audit,
            command.TenantId,
            nameof(TenantEntitlementOverride),
            item.Id,
            existing is null ? "TenantEntitlementOverride.Granted" : "TenantEntitlementOverride.Changed",
            command.ActorUserId,
            correlation.CorrelationId,
            before,
            after: view,
            item.Reason,
            nowUtc,
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        await bus.RecalculateEntitlementsSafelyAsync(command.TenantId, logger, ct);
        return Result.Success(view);
    }
}

/// <summary>Quita la excepción (borra la fila; lo que hubo queda en la auditoría) y recalcula la oficina.</summary>
public static class RemoveTenantEntitlementOverrideHandler
{
    public static async Task<Result> Handle(
        RemoveTenantEntitlementOverrideCommand command,
        ITenantEntitlementOverrideRepository overrides,
        IUnitOfWork unitOfWork,
        ISubscriptionAuditLogWriter audit,
        ICorrelationContext correlation,
        IMessageBus bus,
        ILogger<TenantEntitlementOverride> logger,
        CancellationToken ct
    )
    {
        var reason = command.Reason?.Trim() ?? string.Empty;
        if (reason.Length is 0 or > 500)
            return Result.Failure(
                new Error("EntitlementOverride.ReasonRequired", "A reason of up to 500 characters is required.")
            );

        var item = await overrides.GetForUpdateAsync(command.TenantId, command.Key?.Trim() ?? string.Empty, ct);
        if (item is null)
            return Result.Failure(
                new Error("EntitlementOverride.NotFound", "This office has no override for that key.")
            );

        var nowUtc = DateTime.UtcNow;
        overrides.Remove(item);
        await AuditEntryFactory.AppendAsync(
            audit,
            command.TenantId,
            nameof(TenantEntitlementOverride),
            item.Id,
            "TenantEntitlementOverride.Removed",
            command.ActorUserId,
            correlation.CorrelationId,
            before: TenantEntitlementOverrideView.From(item, nowUtc),
            after: (object?)null,
            reason,
            nowUtc,
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        await bus.RecalculateEntitlementsSafelyAsync(command.TenantId, logger, ct);
        return Result.Success();
    }
}

public static class GetTenantEntitlementOverridesHandler
{
    public static async Task<Result<IReadOnlyList<TenantEntitlementOverrideView>>> Handle(
        GetTenantEntitlementOverridesQuery query,
        ITenantEntitlementOverrideRepository overrides,
        CancellationToken ct
    )
    {
        var nowUtc = DateTime.UtcNow;
        var items = await overrides.GetByTenantIdAsync(query.TenantId, ct);
        return Result.Success<IReadOnlyList<TenantEntitlementOverrideView>>(
            items
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => TenantEntitlementOverrideView.From(item, nowUtc))
                .ToList()
        );
    }
}
