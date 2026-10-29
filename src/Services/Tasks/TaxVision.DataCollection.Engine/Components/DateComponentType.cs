using TaxVision.DataCollection.Engine.Definitions;

namespace TaxVision.DataCollection.Engine.Components;

public sealed class DateComponentType()
    : ComponentTypeBase("date", ComponentKind.Input, DataClass.Personal, ["min", "max"], ComponentOperators.Ordered);
