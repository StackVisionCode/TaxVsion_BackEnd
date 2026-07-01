namespace TaxVision.Subscription.Application.Modules.Dtos;

public sealed class ModuleDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = default!;
    public string Description { get; init; } = default!;
    public string? Url { get; init; }
    public bool IsActive { get; init; }
    public List<Guid> PlanIds { get; init; } = [];
    public List<string> PlanNames { get; init; } = [];
}
