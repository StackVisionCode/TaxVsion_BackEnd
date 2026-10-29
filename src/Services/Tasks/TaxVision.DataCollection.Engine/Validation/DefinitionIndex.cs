using TaxVision.DataCollection.Engine.Components;
using TaxVision.DataCollection.Engine.Definitions;

namespace TaxVision.DataCollection.Engine.Validation;

internal sealed record ComponentContext(
    FormComponent Component,
    FormSection Section,
    string Path,
    int Order,
    string? RepeaterSectionId,
    IComponentType? ComponentType
);

internal sealed record SectionContext(FormSection Section, string Path, int Order);

internal sealed class DefinitionIndex
{
    private DefinitionIndex(
        IReadOnlyDictionary<string, ComponentContext> components,
        IReadOnlyDictionary<string, SectionContext> sections
    )
    {
        Components = components;
        Sections = sections;
    }

    internal IReadOnlyDictionary<string, ComponentContext> Components { get; }
    internal IReadOnlyDictionary<string, SectionContext> Sections { get; }

    internal static DefinitionIndex Create(FormDefinition definition, ComponentRegistry registry)
    {
        var components = new Dictionary<string, ComponentContext>(StringComparer.Ordinal);
        var sections = new Dictionary<string, SectionContext>(StringComparer.Ordinal);
        var order = 0;
        for (var pageIndex = 0; pageIndex < definition.Pages.Count; pageIndex++)
        {
            var page = definition.Pages[pageIndex];
            order++;
            for (var sectionIndex = 0; sectionIndex < page.Sections.Count; sectionIndex++)
            {
                var section = page.Sections[sectionIndex];
                var sectionPath = $"/pages/{pageIndex}/sections/{sectionIndex}";
                order++;
                sections.TryAdd(section.Id, new SectionContext(section, sectionPath, order));
                for (var componentIndex = 0; componentIndex < section.Components.Count; componentIndex++)
                {
                    var component = section.Components[componentIndex];
                    var componentPath = $"{sectionPath}/components/{componentIndex}";
                    order++;
                    registry.TryGet(component.Type, out var componentType);
                    components.TryAdd(
                        component.Id,
                        new ComponentContext(
                            component,
                            section,
                            componentPath,
                            order,
                            section.Repeat is null ? null : section.Id,
                            componentType
                        )
                    );
                }
            }
        }
        return new DefinitionIndex(components, sections);
    }
}
