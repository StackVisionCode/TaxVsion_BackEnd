using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Application.Common;

/// <summary>Validación de límites del plan (asientos e invitaciones) proyectados desde Subscription.</summary>
public static class PlanGuard
{
    /// <summary>
    /// Verifica que el tenant pueda ocupar un asiento adicional
    /// (usuarios activos + invitaciones pendientes &lt; MaxUsers).
    /// Sin proyección de plan (aún no llegó el evento) no se bloquea.
    /// </summary>
    public static async Task<Result> EnsureSeatAvailableAsync(
        Guid tenantId,
        ITenantPlanLimitsStore planLimits,
        IUserRepository users,
        IInvitationRepository invitations,
        CancellationToken ct = default)
    {
        var limits = await planLimits.GetAsync(tenantId, ct);
        if (limits is null)
            return Result.Success();

        if (limits.IsSuspendedForBilling)
        {
            return Result.Failure(
                new Error(
                    "Subscription.Suspended",
                    "Subscription is suspended. Update your payment method to continue."));
        }

        var activeUsers = await users.CountActiveAsync(tenantId, ct);
        var pendingInvitations = await invitations.CountPendingAsync(tenantId, ct);

        if (activeUsers + pendingInvitations >= limits.MaxUsers)
        {
            return Result.Failure(
                new Error(
                    "Plan.UserLimitReached",
                    $"Plan '{limits.PlanCode}' allows up to {limits.MaxUsers} users. " +
                    "Purchase more seats or upgrade your plan."));
        }

        if (pendingInvitations >= limits.MaxPendingInvitations)
        {
            return Result.Failure(
                new Error(
                    "Plan.InvitationLimitReached",
                    $"Plan '{limits.PlanCode}' allows up to {limits.MaxPendingInvitations} pending invitations."));
        }

        return Result.Success();
    }
}
