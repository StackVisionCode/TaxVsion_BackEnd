using TaxVision.DataCollection.Engine.Definitions;

namespace TaxVision.DataCollection.Engine.Components;

public sealed class NumberComponentType()
    : ComponentTypeBase(
        "number",
        ComponentKind.Input,
        DataClass.Financial,
        ["min", "max", "decimals", "unitLabel"],
        ComponentOperators.Ordered
    );
