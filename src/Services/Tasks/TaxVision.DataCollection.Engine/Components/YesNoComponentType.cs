using TaxVision.DataCollection.Engine.Definitions;

namespace TaxVision.DataCollection.Engine.Components;

public sealed class YesNoComponentType()
    : ComponentTypeBase("yesNo", ComponentKind.Input, DataClass.General, ["yesLabel", "noLabel"], ComponentOperators.Boolean);
