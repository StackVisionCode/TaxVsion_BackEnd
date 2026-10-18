using BuildingBlocks.Authorization.SlotGating;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Application.SlotGating.Queries;

public sealed record GetTenantSeatSlotsQuery(Guid TenantId);

public sealed record TenantSeatSlotResponse(
    Guid UserId,
    string FullName,
    string Email,
    bool IsOwner,
    int SlotIndex,
    DateTime? SlotPriorityAt,
    bool IsGated
);

public sealed record TenantSeatSlotsResponse(
    int EffectiveCap,
    string Mode,
    IReadOnlyList<TenantSeatSlotResponse> Slots
);

public static class GetTenantSeatSlotsHandler
{
    public static async Task<Result<TenantSeatSlotsResponse>> Handle(
        GetTenantSeatSlotsQuery query,
        IUserRepository users,
        ITenantPlanLimitsStore planLimits,
        IOptionsMonitor<SlotGatingOptions> options,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        if (query.TenantId == Guid.Empty)
            return Result.Failure<TenantSeatSlotsResponse>(new Error("SeatSlots.Tenant", "Tenant is required."));

        var staff = await users.GetActiveStaffForSlotGatingAsync(query.TenantId, ct);
        var effectiveCap = (await planLimits.GetAsync(query.TenantId, ct))?.MaxUsers ?? 0;
        var calculated = TenantSeatSlotCalculator.Calculate(
            query.TenantId,
            staff.Select(user => new TenantSeatSlotCandidate(user.Id, user.IsOwner, user.SlotPriorityAt)).ToArray(),
            effectiveCap,
            timeProvider.GetUtcNow().UtcDateTime,
            revisionNumber: 0
        );
        var usersById = staff.ToDictionary(user => user.Id);
        var slots = calculated
            .Select(row =>
            {
                var user = usersById[row.UserId];
                return new TenantSeatSlotResponse(
                    user.Id,
                    $"{user.Name} {user.LastName}".Trim(),
                    user.Email,
                    user.IsOwner,
                    row.SlotIndex,
                    user.SlotPriorityAt,
                    row.IsGated
                );
            })
            .ToArray();

        return Result.Success(new TenantSeatSlotsResponse(effectiveCap, options.CurrentValue.Mode.ToString(), slots));
    }
}
