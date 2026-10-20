using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Common;

internal static class OfficeAlertRecipients
{
    public static async Task<IReadOnlyList<Guid>> ResolveOwnerAndSubscribersAsync(
        Guid tenantId,
        string subscriptionPermission,
        RecipientValidation validation,
        IRecipientResolver resolver,
        CancellationToken ct
    )
    {
        var owner = await resolver.ResolveAsync(new TenantOwnerAudience(tenantId), validation, ct);
        var subscribers = await ResolveSubscribersAsync(tenantId, subscriptionPermission, validation, resolver, ct);

        return (owner.IsSuccess ? owner.Value : []).Concat(subscribers).Distinct().ToArray();
    }

    public static async Task<IReadOnlyList<Guid>> ResolveSubscribersAsync(
        Guid tenantId,
        string subscriptionPermission,
        RecipientValidation validation,
        IRecipientResolver resolver,
        CancellationToken ct
    )
    {
        var subscribers = await resolver.ResolveAsync(
            new ByPermission(tenantId, subscriptionPermission),
            validation,
            ct
        );
        return subscribers.IsSuccess ? subscribers.Value : [];
    }
}
