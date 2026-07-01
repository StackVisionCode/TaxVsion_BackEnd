using BuildingBlocks.Domain;

namespace TaxVision.Subscription.Domain.Modules;

/// <summary>
/// Module catalog entity — system-level feature modules (e.g. Tax Returns, Invoicing, Reports).
/// </summary>
public sealed class Module : BaseEntity
{
    public string Name { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public string? Url { get; private set; }
    public bool IsActive { get; private set; } = true;

    private readonly List<PlanModule> _planModules = [];
    public IReadOnlyList<PlanModule> PlanModules => _planModules.AsReadOnly();

    private Module() { }

    public static Module Create(string name, string description, string? url = null, bool isActive = true)
    {
        return new Module
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Description = description.Trim(),
            Url = url?.Trim(),
            IsActive = isActive
        };
    }

    public void Update(string name, string description, string? url, bool isActive)
    {
        Name = name.Trim();
        Description = description.Trim();
        Url = url?.Trim();
        IsActive = isActive;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
    public void SoftDelete() => IsActive = false;
}
