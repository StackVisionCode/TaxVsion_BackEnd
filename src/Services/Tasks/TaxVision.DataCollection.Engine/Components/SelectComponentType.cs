using TaxVision.DataCollection.Engine.Definitions;

namespace TaxVision.DataCollection.Engine.Components;

public sealed class SelectComponentType()
    : ComponentTypeBase(
        "select",
        ComponentKind.Input,
        DataClass.General,
        ["options", "placeholder"],
        ComponentOperators.Choice
    );
