using BuildingBlocks.Domain;
using BuildingBlocks.Results;

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

    public static Result<Module> Create(string name, string description, string? url = null, bool isActive = true)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Module>(new Error("Module.NameRequired", "Name is required."));

        if (string.IsNullOrWhiteSpace(description))
            return Result.Failure<Module>(new Error("Module.DescriptionRequired", "Description is required."));

        return Result.Success(new Module
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Description = description.Trim(),
            Url = url?.Trim(),
            IsActive = isActive
        });
    }

    public Result Update(string name, string description, string? url, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(new Error("Module.NameRequired", "Name is required."));

        if (string.IsNullOrWhiteSpace(description))
            return Result.Failure(new Error("Module.DescriptionRequired", "Description is required."));

        Name = name.Trim();
        Description = description.Trim();
        Url = url?.Trim();
        IsActive = isActive;
        return Result.Success();
    }

    public Result Activate()
    {
        IsActive = true;
        return Result.Success();
    }

    public Result Deactivate()
    {
        IsActive = false;
        return Result.Success();
    }

    public Result SoftDelete()
    {
        IsActive = false;
        return Result.Success();
    }
}
