using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TaxVision.Auth.Application.SlotGating;
using TaxVision.Auth.Application.SlotGating.Abstractions;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Infrastructure.Persistence;

namespace TaxVision.Auth.Api.Health;

public sealed class SeatSlotProjectionHealthCheck(AuthDbContext db, TimeProvider timeProvider) : IHealthCheck
{
    private static readonly TimeSpan AllowedLag = TimeSpan.FromMinutes(10);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        var tenantLimits = await db
            .TenantPlanLimits.AsNoTracking()
            .Where(limits => limits.MaxUsers > 0)
            .Select(limits => new LimitState(limits.Id, limits.MaxUsers, limits.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
        if (tenantLimits.Count == 0)
            return HealthCheckResult.Healthy("No seat-bearing tenants require a projection.");

        var tenantIds = tenantLimits.Select(item => item.TenantId).ToArray();
        var staff = await db
            .Users.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(user =>
                tenantIds.Contains(user.TenantId)
                && user.IsActive
                && (user.ActorType == UserActorType.TenantAdmin || user.ActorType == UserActorType.TenantEmployee)
            )
            .Select(user => new StaffState(
                user.TenantId,
                user.Id,
                user.ActorType,
                user.SlotPriorityAt,
                user.CreatedAtUtc
            ))
            .ToListAsync(cancellationToken);
        var projections = await db
            .TenantSeatSlotProjections.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(row => tenantIds.Contains(row.TenantId))
            .Select(row => new ProjectionState(row.TenantId, row.UserId, row.SlotIndex, row.EffectiveCap, row.IsGated))
            .ToListAsync(cancellationToken);
        var latestOverrides = await db
            .AuthAuditLogs.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(log => tenantIds.Contains(log.TenantId) && log.Action == AuthAuditAction.UserSlotPriorityOverridden)
            .GroupBy(log => log.TenantId)
            .Select(group => new { TenantId = group.Key, OccurredAtUtc = group.Max(log => log.OccurredAtUtc) })
            .ToDictionaryAsync(row => row.TenantId, row => row.OccurredAtUtc, cancellationToken);
        var latestDeactivations = await db
            .Users.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(user => tenantIds.Contains(user.TenantId) && user.DeactivatedAtUtc != null)
            .GroupBy(user => user.TenantId)
            .Select(group => new
            {
                TenantId = group.Key,
                DeactivatedAtUtc = group.Max(user => user.DeactivatedAtUtc)!.Value,
            })
            .ToDictionaryAsync(row => row.TenantId, row => row.DeactivatedAtUtc, cancellationToken);

        var cutoffUtc = timeProvider.GetUtcNow().UtcDateTime.Subtract(AllowedLag);
        var staleTenantIds = new List<Guid>();
        var pendingTenantIds = new List<Guid>();

        foreach (var limits in tenantLimits)
        {
            var tenantStaff = staff.Where(user => user.TenantId == limits.TenantId).ToArray();
            var current = projections.Where(row => row.TenantId == limits.TenantId).ToArray();
            var desired = TenantSeatSlotCalculator.Calculate(
                limits.TenantId,
                tenantStaff
                    .Select(user => new TenantSeatSlotCandidate(
                        user.UserId,
                        user.ActorType == UserActorType.TenantAdmin && user.SlotPriorityAt == DateTime.MinValue,
                        user.SlotPriorityAt
                    ))
                    .ToArray(),
                limits.MaxUsers,
                computedAtUtc: DateTime.MinValue,
                revisionNumber: 0
            );

            if (HasSameDecisionState(current, desired))
                continue;

            var latestSourceAtUtc = tenantStaff
                .Select(user => user.CreatedAtUtc)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();
            latestSourceAtUtc = Max(latestSourceAtUtc, limits.UpdatedAtUtc);
            if (latestOverrides.TryGetValue(limits.TenantId, out var latestOverrideAtUtc))
                latestSourceAtUtc = Max(latestSourceAtUtc, latestOverrideAtUtc);
            if (latestDeactivations.TryGetValue(limits.TenantId, out var latestDeactivationAtUtc))
                latestSourceAtUtc = Max(latestSourceAtUtc, latestDeactivationAtUtc);

            if (latestSourceAtUtc <= cutoffUtc)
                staleTenantIds.Add(limits.TenantId);
            else
                pendingTenantIds.Add(limits.TenantId);
        }

        var data = new Dictionary<string, object>
        {
            ["staleTenantCount"] = staleTenantIds.Count,
            ["pendingTenantCount"] = pendingTenantIds.Count,
            ["staleTenantSample"] = staleTenantIds.Take(10).Select(id => id.ToString()).ToArray(),
        };
        return staleTenantIds.Count > 0
            ? HealthCheckResult.Unhealthy("Seat slot projections are stale beyond the allowed lag.", data: data)
            : HealthCheckResult.Healthy("Seat slot projections are current or inside the reconciliation window.", data);
    }

    private static bool HasSameDecisionState(
        IReadOnlyCollection<ProjectionState> current,
        IReadOnlyCollection<TenantSeatSlotProjectionRecord> desired
    )
    {
        if (current.Count != desired.Count)
            return false;

        var currentByUser = current.ToDictionary(row => row.UserId);
        return desired.All(row =>
            currentByUser.TryGetValue(row.UserId, out var existing)
            && existing.SlotIndex == row.SlotIndex
            && existing.EffectiveCap == row.EffectiveCap
            && existing.IsGated == row.IsGated
        );
    }

    private static DateTime Max(DateTime left, DateTime right) => left >= right ? left : right;

    private sealed record LimitState(Guid TenantId, int MaxUsers, DateTime UpdatedAtUtc);

    private sealed record StaffState(
        Guid TenantId,
        Guid UserId,
        UserActorType ActorType,
        DateTime? SlotPriorityAt,
        DateTime CreatedAtUtc
    );

    private sealed record ProjectionState(Guid TenantId, Guid UserId, int SlotIndex, int EffectiveCap, bool IsGated);
}
