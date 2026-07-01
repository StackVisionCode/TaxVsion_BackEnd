namespace TaxVision.Subscription.Application.SubscriptionModules.Dtos;

public sealed class AssignModuleRequest
{
    public required Guid SubscriptionId { get; init; }
    public required Guid ModuleId { get; init; }
    public bool IsIncluded { get; init; } = true;
}
