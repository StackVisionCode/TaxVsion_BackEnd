using TaxVision.DataCollection.Engine.Definitions;

namespace TaxVision.DataCollection.Engine.Components;

public sealed class RadioComponentType()
    : ComponentTypeBase(
        "radio",
        ComponentKind.Input,
        DataClass.General,
        ["options", "layout"],
        ComponentOperators.Choice
    );
