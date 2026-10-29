using TaxVision.DataCollection.Engine.Definitions;

namespace TaxVision.DataCollection.Engine.Components;

public sealed class CurrencyComponentType()
    : ComponentTypeBase(
        "currency",
        ComponentKind.Input,
        DataClass.Financial,
        ["currency", "min", "max", "allowNegative"],
        ComponentOperators.Ordered
    );
