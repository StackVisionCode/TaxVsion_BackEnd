using TaxVision.DataCollection.Engine.Definitions;

namespace TaxVision.DataCollection.Engine.Components;

public sealed class CheckboxGroupComponentType()
    : ComponentTypeBase(
        "checkboxGroup",
        ComponentKind.Input,
        DataClass.General,
        ["options", "minSelected", "maxSelected"],
        ComponentOperators.MultipleChoice
    );
