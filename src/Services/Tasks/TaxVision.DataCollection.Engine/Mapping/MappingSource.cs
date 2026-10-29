using TaxVision.DataCollection.Engine.Components;
using TaxVision.DataCollection.Engine.Definitions;

namespace TaxVision.DataCollection.Engine.Mapping;

// Componente mapeable con su sección y página: la visibilidad de las tres decide si queda respondido.
internal sealed record MappingSource(FormComponent Component, IComponentType? Type, FormSection Section, FormPage Page)
{
    internal bool InRepeater => Section.Repeat is not null;

    internal DataClass? EffectiveDataClass => Component.DataClass ?? Type?.MinimumDataClass;

    internal bool IsRestricted => Component.Type == "taxId" || EffectiveDataClass == DataClass.Restricted;
}
