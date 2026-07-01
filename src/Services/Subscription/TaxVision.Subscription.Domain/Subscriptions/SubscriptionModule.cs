using BuildingBlocks.Domain;

namespace TaxVision.Subscription.Domain.Subscriptions;

/// <summary>
/// Represents a module enabled/disabled for a subscription.
/// IsIncluded = true means the module is active for the tenant.
/// </summary>
public sealed class SubscriptionModule : BaseEntity
{
    public Guid SubscriptionId { get; private set; }
    public Guid ModuleId { get; private set; }
    public bool IsIncluded { get; private set; } = true;

    private SubscriptionModule() { }

    public static SubscriptionModule Create(Guid subscriptionId, Guid moduleId, bool isIncluded = true)
        => new()
        {
            Id = Guid.NewGuid(),
            SubscriptionId = subscriptionId,
            ModuleId = moduleId,
            IsIncluded = isIncluded
        };

    public void SetIncluded(bool isIncluded) => IsIncluded = isIncluded;
}
