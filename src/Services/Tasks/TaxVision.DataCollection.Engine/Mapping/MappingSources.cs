using TaxVision.DataCollection.Engine.Components;
using TaxVision.DataCollection.Engine.Definitions;

namespace TaxVision.DataCollection.Engine.Mapping;

internal sealed class MappingSources
{
    private readonly Dictionary<string, MappingSource> _components = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (FormSection Section, FormPage Page)> _sections = new(StringComparer.Ordinal);

    private MappingSources() { }

    internal static MappingSources Create(FormDefinition definition, ComponentRegistry registry)
    {
        var sources = new MappingSources();
        foreach (var page in definition.Pages ?? [])
        foreach (var section in page.Sections ?? [])
        {
            sources._sections.TryAdd(section.Id, (section, page));
            foreach (var component in section.Components ?? [])
            {
                registry.TryGet(component.Type, out var type);
                sources._components.TryAdd(component.Id, new MappingSource(component, type, section, page));
            }
        }
        return sources;
    }

    internal MappingSource? Component(string? id) =>
        id is not null && _components.TryGetValue(id, out var source) ? source : null;

    internal (FormSection Section, FormPage Page)? Section(string? id) =>
        id is not null && _sections.TryGetValue(id, out var section) ? section : null;
}
