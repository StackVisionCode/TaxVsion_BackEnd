using BuildingBlocks.Authorization.SlotGating;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Application.Consumers;

public static class UserSlotGatedEmailConsumer
{
    private static readonly TimeSpan DedupeWindow = TimeSpan.FromHours(24);

    public static async Task Handle(
        UserSlotGatedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ITenantOwnerProjectionRepository offices,
        IUserEmailDirectoryRepository directory,
        IScribeRenderClient scribeClient,
        ISeatGateNotificationLogRepository notificationLogs,
        IOptionsMonitor<SlotGatingOptions> options,
        IPlatformSettings platform,
        IRecipientLocale locales,
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

            // El render va antes de la primera escritura.
            var billingUrl = BuildBillingUrl(evt.TenantSubdomain, platform);
            var locale = await locales.ForOfficeEmailAsync(evt.TenantId, evt.PrimaryAdminEmail!, ct);
            var render = (
                await scribeClient.RenderForRecipientAsync(
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
                    ScribeRenderAudience.Staff,
                    locale,
                    ct
                )
            ).EnsureRendered("seats.user_gated.v1");

            if (openLog is null)
                await notificationLogs.AddAsync(
                    SeatGateNotificationLog.Create(evt.TenantId, evt.UserId, evt.EventId, nowUtc),
                    ct
                );
            else
                openLog.Renew(evt.EventId, nowUtc);

            await SeatSlotEmails.SendAsync(
                new NotificationIntent(
                    NotificationKinds.AuthUserSlotGated,
                    evt.TenantId,
                    new NotificationTarget(Locale: locale),
                    evt.EventId,
                    nameof(UserSlotGatedIntegrationEvent),
                    evt.OccurredOn,
                    correlation.CorrelationId
                ),
                "seats.user_gated.v1",
                evt.PrimaryAdminEmail!,
                render,
                dispatcher,
                offices,
                directory,
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
        NotificationDispatcher dispatcher,
        ITenantOwnerProjectionRepository offices,
        IUserEmailDirectoryRepository directory,
        IScribeRenderClient scribeClient,
        ISeatGateNotificationLogRepository notificationLogs,
        IUnitOfWork unitOfWork,
        IOptionsMonitor<SlotGatingOptions> options,
        IPlatformSettings platform,
        IRecipientLocale locales,
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
            var locale = await locales.ForOfficeEmailAsync(evt.TenantId, evt.PrimaryAdminEmail, ct);
            var render = (
                await scribeClient.RenderForRecipientAsync(
                    "seats.user_unlocked.v1",
                    evt.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["userFullName"] = evt.UserFullName,
                        ["userEmail"] = evt.UserEmail,
                        ["tenantName"] = evt.TenantName ?? platform.Current.BrandName,
                    },
                    ScribeRenderAudience.Staff,
                    locale,
                    ct
                )
            ).EnsureRendered("seats.user_unlocked.v1");

            await SeatSlotEmails.SendAsync(
                new NotificationIntent(
                    NotificationKinds.AuthUserSlotUnlocked,
                    evt.TenantId,
                    new NotificationTarget(Locale: locale),
                    evt.EventId,
                    nameof(UserSlotUnlockedIntegrationEvent),
                    evt.OccurredOn,
                    correlation.CorrelationId
                ),
                "seats.user_unlocked.v1",
                evt.PrimaryAdminEmail,
                render,
                dispatcher,
                offices,
                directory,
                ct
            );
        }
    }
}

/// <summary>
/// El correo al admin principal: si la dirección es de un owner o admin activo, va con su usuario y respeta su
/// preferencia de correo de facturación.
/// </summary>
internal static class SeatSlotEmails
{
    public static async Task SendAsync(
        NotificationIntent intent,
        string templateKey,
        string adminEmail,
        ScribeRenderedEmail render,
        NotificationDispatcher dispatcher,
        ITenantOwnerProjectionRepository offices,
        IUserEmailDirectoryRepository directory,
        CancellationToken ct
    )
    {
        var userId = await OfficeEmailUsers.FindAsync(intent.TenantId, adminEmail, offices, directory, ct);
        await dispatcher.DispatchAsync(
            intent with
            {
                Target = intent.Target with { UserId = userId, Email = adminEmail },
                Email = new EmailContent(
                    templateKey,
                    new Dictionary<string, object?>(),
                    Render: (_, _) => Task.FromResult(render)
                ),
            },
            ct
        );
    }
}
