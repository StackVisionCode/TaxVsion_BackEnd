namespace TaxVision.Subscription.Application.SubscriptionModules.Dtos;

public sealed class SubscriptionModuleDto
{
    public Guid Id { get; init; }
    public Guid SubscriptionId { get; init; }
    public Guid ModuleId { get; init; }
    public bool IsIncluded { get; init; }
    public string? ModuleName { get; init; }
    public string? ModuleDescription { get; init; }
    public string? ModuleUrl { get; init; }
}
