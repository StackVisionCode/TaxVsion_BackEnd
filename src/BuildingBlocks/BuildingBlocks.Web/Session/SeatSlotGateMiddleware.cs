using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization.SlotGating;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Web.Session;

public sealed class SeatSlotGateMiddleware(
    RequestDelegate next,
    IOptionsMonitor<SlotGatingOptions> options,
    IHostEnvironment environment,
    ILogger<SeatSlotGateMiddleware> logger
)
{
    private static readonly PathString[] ExemptPaths =
    [
        new("/auth/me"),
        new("/auth/logout"),
        new("/health"),
        new("/internal"),
    ];

    public async Task InvokeAsync(HttpContext context, ISeatSlotGate gate)
    {
        var mode = options.CurrentValue.Mode;
        if (mode == SlotGatingMode.Disabled || IsExempt(context))
        {
            await next(context);
            return;
        }

        if (!context.User.TryGetTenantId(out var tenantId) || !context.User.TryGetUserId(out var userId))
        {
            await next(context);
            return;
        }

        SeatSlotDecision decision;
        try
        {
            decision = await gate.CheckAsync(tenantId, userId, context.RequestAborted);
        }
        catch (Exception exception)
        {
            SeatSlotGateMetrics.Failures.Add(
                1,
                new KeyValuePair<string, object?>("tenant_id", tenantId),
                new KeyValuePair<string, object?>("mode", mode.ToString())
            );
            logger.LogError(
                exception,
                "Seat slot gate check failed for tenant {TenantId} user {UserId} in {Mode} mode.",
                tenantId,
                userId,
                mode
            );
            await next(context);
            return;
        }

        SeatSlotGateMetrics.Decisions.Add(
            1,
            new("tenant_id", tenantId),
            new("mode", mode.ToString()),
            new("outcome", decision.Allowed ? "allowed" : "gated")
        );

        if (decision.Allowed)
        {
            await next(context);
            return;
        }
        logger.LogWarning(
            "Seat slot gated tenant {TenantId} user {UserId} at slot {SlotIndex} with cap {EffectiveCap} in {Mode} mode.",
            tenantId,
            userId,
            decision.SlotIndex,
            decision.EffectiveCap,
            mode
        );

        if (mode == SlotGatingMode.Shadow)
        {
            if (environment.IsDevelopment())
                context.Response.Headers["X-Seat-Slot-Gated"] = "true";

            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(
            new
            {
                error = "seats.user_slot_gated",
                message = "Your seat is suspended due to unpaid add-on. Ask the account owner to pay or reorder slots.",
                slotIndex = decision.SlotIndex,
                effectiveCap = decision.EffectiveCap,
                contactAdminUrl = $"{context.Request.Scheme}://{context.Request.Host}/account/users",
            },
            context.RequestAborted
        );
    }

    private static bool IsExempt(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            return true;

        var actorType = context.User.GetActorType();
        if (actorType is ActorType.CustomerPortal or ActorType.PlatformAdmin or ActorType.Service)
            return true;

        if (
            string.Equals(context.User.FindFirst(ClaimNames.IsOwner)?.Value, "true", StringComparison.OrdinalIgnoreCase)
        )
            return true;

        return ExemptPaths.Any(path => context.Request.Path.StartsWithSegments(path));
    }
}
