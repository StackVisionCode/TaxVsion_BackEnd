namespace TaxVision.Subscription.Application.Modules.Dtos;

public sealed class UpdateModuleRequest
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public string? Url { get; init; }
    public bool IsActive { get; init; } = true;
}
