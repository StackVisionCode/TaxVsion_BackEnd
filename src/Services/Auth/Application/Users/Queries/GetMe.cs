using System.Text.Json;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;

namespace TaxVision.Auth.Application.Users.Queries;

public sealed record MeTenantResponse(Guid Id, string Name, string SubDomain);

public sealed record MePlanResponse(
    string Code,
    int MaxUsers,
    int ActiveUsers,
    int PendingInvitations,
    bool IsSuspendedForBilling,
    IReadOnlyList<string> EnabledModules
);

public sealed record MeResponse(
    Guid Id,
    string Name,
    string LastName,
    string Email,
    string ActorType,
    Guid? CustomerId,
    MeTenantResponse Tenant,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    string TimeZoneId,
    bool MfaEnabled,
    bool EmailVerified,
    bool PhoneVerified,
    string? PhoneNumber,
    MePlanResponse? Plan
);

public sealed record GetMeQuery(Guid UserId);

public static class GetMeHandler
{
    public static async Task<Result<MeResponse>> Handle(
        GetMeQuery query,
        IUserRepository users,
        ITenantRegistry tenants,
        IRoleRepository roles,
        ITenantPlanLimitsStore planLimits,
        IInvitationRepository invitations,
        CancellationToken ct
    )
    {
        var user = await users.GetByIdAsync(query.UserId, ct);
        if (user is null || !user.IsActive)
            return Result.Failure<MeResponse>(new Error("User.NotFound", "User does not exist."));

        var tenant = await tenants.GetByIdAsync(user.TenantId, ct);
        if (tenant is null)
            return Result.Failure<MeResponse>(new Error("Tenant.NotFound", "Tenant does not exist."));

        var (roleNames, permissions) = await UserAccessResolver.ResolveAsync(user, roles, ct);

        MePlanResponse? plan = null;
        var limits = await planLimits.GetAsync(user.TenantId, ct);
        if (limits is not null)
        {
            var activeUsers = await users.CountActiveAsync(user.TenantId, ct);
            var pending = await invitations.CountPendingAsync(user.TenantId, ct);
            var modules = JsonSerializer.Deserialize<List<string>>(limits.EnabledModulesJson) ?? [];
            plan = new MePlanResponse(
                limits.PlanCode,
                limits.MaxUsers,
                activeUsers,
                pending,
                limits.IsSuspendedForBilling,
                modules
            );
        }

        return Result.Success(
            new MeResponse(
                user.Id,
                user.Name,
                user.LastName,
                user.Email,
                user.ActorType.ToString(),
                user.CustomerId,
                new MeTenantResponse(tenant.Id, tenant.Name, tenant.SubDomain),
                roleNames,
                permissions,
                UserAccessResolver.EffectiveTimeZone(user, tenant),
                user.MfaEnabled,
                user.EmailVerified,
                user.PhoneVerified,
                user.PhoneNumber,
                plan
            )
        );
    }
}
