using BuildingBlocks.Authorization.SlotGating;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Application.Consumers;

public static class UserSlotGatedEmailConsumer
{
    private static readonly TimeSpan DedupeWindow = TimeSpan.FromHours(24);

    public static async Task Handle(
        UserSlotGatedIntegrationEvent evt,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        ISeatGateNotificationLogRepository notificationLogs,
        IOptionsMonitor<SlotGatingOptions> options,
        IPlatformSettings platform,
        ICorrelationContext correlation,
        TimeProvider timeProvider,
        ILogger<UserSlotGatedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        if (!options.CurrentValue.EmitNotifications)
            return;

        if (!HasNotificationContext(evt.PrimaryAdminEmail, evt.UserFullName, evt.UserEmail))
        {
            logger.LogWarning(
                "Seat gate email skipped for user {UserId} in tenant {TenantId}: notification context is incomplete.",
                evt.UserId,
                evt.TenantId
            );
            return;
        }

        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            var openLog = await notificationLogs.GetOpenAsync(evt.TenantId, evt.UserId, ct);
            if (openLog?.WasNotifiedWithin(nowUtc.Subtract(DedupeWindow)) == true)
            {
                logger.LogInformation(
                    "Seat gate email suppressed for user {UserId} in tenant {TenantId}: an unresolved notification exists within 24 hours.",
                    evt.UserId,
                    evt.TenantId
                );
                return;
            }

            if (openLog is null)
                await notificationLogs.AddAsync(
                    SeatGateNotificationLog.Create(evt.TenantId, evt.UserId, evt.EventId, nowUtc),
                    ct
                );
            else
                openLog.Renew(evt.EventId, nowUtc);

            var billingUrl = BuildBillingUrl(evt.TenantSubdomain, platform);
            var render = (
                await scribeClient.RenderAsync(
                    "seats.user_gated.v1",
                    evt.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["userFullName"] = evt.UserFullName,
                        ["userEmail"] = evt.UserEmail,
                        ["slotIndex"] = evt.SlotIndex,
                        ["effectiveCap"] = evt.EffectiveCap,
                        ["tenantName"] = evt.TenantName ?? platform.Current.BrandName,
                        ["billingUrl"] = billingUrl,
                    },
                    ct
                )
            ).EnsureRendered("seats.user_gated.v1");

            await gateway.QueueEmailAsync(
                render.ToDispatchRequest(
                    evt.TenantId,
                    evt.PrimaryAdminEmail!,
                    "seats.user_gated.v1",
                    evt.EventId,
                    correlation.CorrelationId
                ),
                ct
            );
        }
    }

    private static bool HasNotificationContext(string? adminEmail, string? userFullName, string? userEmail) =>
        !string.IsNullOrWhiteSpace(adminEmail)
        && !string.IsNullOrWhiteSpace(userFullName)
        && !string.IsNullOrWhiteSpace(userEmail);

    private static string BuildBillingUrl(string? tenantSubdomain, IPlatformSettings platform) =>
        string.IsNullOrWhiteSpace(tenantSubdomain)
            ? platform.Current.Website
            : $"https://{platform.Current.TenantHost(tenantSubdomain)}/account/manage-subscription";
}

public static class UserSlotUnlockedEmailConsumer
{
    public static async Task Handle(
        UserSlotUnlockedIntegrationEvent evt,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        ISeatGateNotificationLogRepository notificationLogs,
        IUnitOfWork unitOfWork,
        IOptionsMonitor<SlotGatingOptions> options,
        IPlatformSettings platform,
        ICorrelationContext correlation,
        TimeProvider timeProvider,
        ILogger<UserSlotUnlockedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        var openLog = await notificationLogs.GetOpenAsync(evt.TenantId, evt.UserId, ct);
        openLog?.Resolve(timeProvider.GetUtcNow().UtcDateTime);

        if (!options.CurrentValue.EmitNotifications)
        {
            if (openLog is not null)
                await unitOfWork.SaveChangesAsync(ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(evt.PrimaryAdminEmail) || string.IsNullOrWhiteSpace(evt.UserFullName))
        {
            if (openLog is not null)
                await unitOfWork.SaveChangesAsync(ct);
            logger.LogWarning(
                "Seat unlock email skipped for user {UserId} in tenant {TenantId}: notification context is incomplete.",
                evt.UserId,
                evt.TenantId
            );
            return;
        }

        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var render = (
                await scribeClient.RenderAsync(
                    "seats.user_unlocked.v1",
                    evt.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["userFullName"] = evt.UserFullName,
                        ["userEmail"] = evt.UserEmail,
                        ["tenantName"] = evt.TenantName ?? platform.Current.BrandName,
                    },
                    ct
                )
            ).EnsureRendered("seats.user_unlocked.v1");

            await gateway.QueueEmailAsync(
                render.ToDispatchRequest(
                    evt.TenantId,
                    evt.PrimaryAdminEmail,
                    "seats.user_unlocked.v1",
                    evt.EventId,
                    correlation.CorrelationId
                ),
                ct
            );
        }
    }
}
