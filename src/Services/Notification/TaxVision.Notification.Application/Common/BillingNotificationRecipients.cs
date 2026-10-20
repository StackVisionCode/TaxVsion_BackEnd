using BuildingBlocks.Authorization;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Common;

internal static class BillingNotificationRecipients
{
    public static async Task<IReadOnlyList<Guid>> ResolveOwnerAndSubscribersAsync(
        Guid tenantId,
        IRecipientResolver resolver,
        CancellationToken ct
    ) =>
        await OfficeAlertRecipients.ResolveOwnerAndSubscribersAsync(
            tenantId,
            NotificationPermissions.BillingAlerts,
            BillingValidation,
            resolver,
            ct
        );

    public static async Task<IReadOnlyList<Guid>> ResolveSubscribersAsync(
        Guid tenantId,
        IRecipientResolver resolver,
        CancellationToken ct
    ) =>
        await OfficeAlertRecipients.ResolveSubscribersAsync(
            tenantId,
            NotificationPermissions.BillingAlerts,
            BillingValidation,
            resolver,
            ct
        );

    private static readonly RecipientValidation BillingValidation = new(
        RequiredCapability: SubscriptionPermissions.BillingView,
        RequiredModule: PermissionModuleMap.ModuleFor(SubscriptionPermissions.BillingView)
    );
}
