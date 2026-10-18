using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.SlotGating.Abstractions;
using Wolverine;

namespace TaxVision.Auth.Application.SlotGating.Commands;

public sealed record RecomputeTenantSeatSlotsCommand(Guid TenantId, bool RefreshUnchanged = false);

// G-B8: proyección + outbox (SlotGated/SlotUnlocked) commitean atómicos — [Transactional] sería
// redundante aquí porque Auth tiene `options.Policies.AutoApplyTransactions()` en Program.cs
// (envuelve todo handler con un scope EF Core + outbox de Wolverine).
public static class RecomputeTenantSeatSlotsHandler
{
    public static async Task<Result> Handle(
        RecomputeTenantSeatSlotsCommand command,
        IUserRepository users,
        ITenantPlanLimitsStore planLimits,
        ITenantSeatSlotProjectionRepository projections,
        ISeatSlotRecomputeSerializer serializer,
        ITenantRegistry tenants,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        if (command.TenantId == Guid.Empty)
            return Result.Failure(new Error("SeatSlots.Tenant", "Tenant is required."));

        await using var _ = await serializer.AcquireAsync(command.TenantId, ct);
        var staff = await users.GetActiveStaffForSlotGatingAsync(command.TenantId, ct);
        var current = await projections.GetByTenantAsync(command.TenantId, ct);
        var limits = await planLimits.GetAsync(command.TenantId, ct);
        var revision = current.Count == 0 ? 1 : current.Max(row => row.RevisionNumber) + 1;
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var calculated = TenantSeatSlotCalculator.Calculate(
            command.TenantId,
            staff.Select(user => new TenantSeatSlotCandidate(user.Id, user.IsOwner, user.SlotPriorityAt)).ToArray(),
            limits?.MaxUsers ?? 0,
            nowUtc,
            revision
        );

        if (!command.RefreshUnchanged && HasSameDecisionState(current, calculated))
            return Result.Success();

        await projections.ReplaceAsync(command.TenantId, calculated, ct);
        var previousByUser = current.ToDictionary(row => row.UserId);
        var transitions = calculated
            .Where(row =>
                (row.IsGated && (!previousByUser.TryGetValue(row.UserId, out var previous) || !previous.IsGated))
                || (!row.IsGated && previousByUser.TryGetValue(row.UserId, out previous) && previous.IsGated)
            )
            .ToArray();
        var staffById = staff.ToDictionary(user => user.Id);
        var primaryAdmin = transitions.Length == 0 ? null : await users.GetPrimaryAdminAsync(command.TenantId, ct);
        var tenant = transitions.Length == 0 ? null : await tenants.GetByIdAsync(command.TenantId, ct);

        foreach (var row in transitions)
        {
            previousByUser.TryGetValue(row.UserId, out var previous);
            staffById.TryGetValue(row.UserId, out var user);
            if (row.IsGated && previous?.IsGated != true)
            {
                await bus.PublishAsync(
                    new UserSlotGatedIntegrationEvent
                    {
                        TenantId = row.TenantId,
                        UserId = row.UserId,
                        SlotIndex = row.SlotIndex,
                        EffectiveCap = row.EffectiveCap,
                        UserFullName = UserFullName(user),
                        UserEmail = user?.Email,
                        PrimaryAdminEmail = primaryAdmin?.Email,
                        TenantName = tenant?.Name,
                        TenantSubdomain = tenant?.SubDomain,
                        CorrelationId = correlation.CorrelationId,
                    }
                );
            }
            else if (!row.IsGated && previous?.IsGated == true)
            {
                await bus.PublishAsync(
                    new UserSlotUnlockedIntegrationEvent
                    {
                        TenantId = row.TenantId,
                        UserId = row.UserId,
                        UserFullName = UserFullName(user),
                        UserEmail = user?.Email,
                        PrimaryAdminEmail = primaryAdmin?.Email,
                        TenantName = tenant?.Name,
                        TenantSubdomain = tenant?.SubDomain,
                        CorrelationId = correlation.CorrelationId,
                    }
                );
            }
        }

        await unitOfWork.SaveChangesAsync(ct);
        SeatSlotProjectionMetrics.Record(
            command.TenantId,
            calculated.Count,
            calculated.FirstOrDefault()?.EffectiveCap ?? Math.Max(0, limits?.MaxUsers ?? 0),
            calculated.Count(row => row.IsGated)
        );
        return Result.Success();
    }

    private static string? UserFullName(Domain.Users.User? user) =>
        user is null ? null : $"{user.Name} {user.LastName}".Trim();

    private static bool HasSameDecisionState(
        IReadOnlyList<TenantSeatSlotProjectionRecord> current,
        IReadOnlyList<TenantSeatSlotProjectionRecord> calculated
    )
    {
        if (current.Count != calculated.Count)
            return false;

        var currentByUser = current.ToDictionary(row => row.UserId);
        return calculated.All(row =>
            currentByUser.TryGetValue(row.UserId, out var existing)
            && existing.SlotIndex == row.SlotIndex
            && existing.EffectiveCap == row.EffectiveCap
            && existing.IsGated == row.IsGated
        );
    }
}
