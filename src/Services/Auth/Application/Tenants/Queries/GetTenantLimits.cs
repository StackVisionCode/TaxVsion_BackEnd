using System.Text.Json;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Application.Tenants.Queries;

public sealed record TenantLimitsResponse(
    string? PlanCode,
    int? MaxUsers,
    int ActiveUsers,
    int PendingInvitations,
    int? AvailableSeats,
    int? MaxPendingInvitations,
    long? StorageQuotaBytes,
    bool IsSuspendedForBilling,
    IReadOnlyList<string> EnabledModules);

public sealed record GetTenantLimitsQuery(Guid TenantId);

public static class GetTenantLimitsHandler
{
    public static async Task<Result<TenantLimitsResponse>> Handle(
        GetTenantLimitsQuery query,
        ITenantPlanLimitsStore planLimits,
        IUserRepository users,
        IInvitationRepository invitations,
        CancellationToken ct)
    {
        var activeUsers = await users.CountActiveAsync(query.TenantId, ct);
        var pending = await invitations.CountPendingAsync(query.TenantId, ct);

        var limits = await planLimits.GetAsync(query.TenantId, ct);
        if (limits is null)
        {
            return Result.Success(new TenantLimitsResponse(
                null, null, activeUsers, pending, null, null, null, false, []));
        }

        var modules = JsonSerializer.Deserialize<List<string>>(limits.EnabledModulesJson) ?? [];
        return Result.Success(new TenantLimitsResponse(
            limits.PlanCode,
            limits.MaxUsers,
            activeUsers,
            pending,
            Math.Max(0, limits.MaxUsers - activeUsers - pending),
            limits.MaxPendingInvitations,
            limits.StorageQuotaBytes,
            limits.IsSuspendedForBilling,
            modules));
    }
}
