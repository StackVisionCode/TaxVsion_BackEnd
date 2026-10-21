using TaxVision.DataCollection.Engine.Definitions;

namespace TaxVision.DataCollection.Engine.Components;

public enum ComponentKind
{
    Layout,
    Compliance,
    Input,
}

public interface IComponentType
{
    string Name { get; }
    ComponentKind Kind { get; }
    DataClass? MinimumDataClass { get; }
    IReadOnlySet<string> AllowedProperties { get; }
    IReadOnlySet<string> AllowedOperators { get; }
}

public abstract class ComponentTypeBase : IComponentType
{
    protected ComponentTypeBase(
        string name,
        ComponentKind kind,
        DataClass? minimumDataClass,
        IEnumerable<string>? properties = null,
        IEnumerable<string>? operators = null
    )
    {
        Name = name;
        Kind = kind;
        MinimumDataClass = minimumDataClass;
        AllowedProperties = new HashSet<string>(properties ?? [], StringComparer.Ordinal);
        AllowedOperators = new HashSet<string>(operators ?? [], StringComparer.Ordinal);
    }

    public string Name { get; }
    public ComponentKind Kind { get; }
    public DataClass? MinimumDataClass { get; }
    public IReadOnlySet<string> AllowedProperties { get; }
    public IReadOnlySet<string> AllowedOperators { get; }
}
