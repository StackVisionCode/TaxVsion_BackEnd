using TaxVision.DataCollection.Engine.Definitions;

namespace TaxVision.DataCollection.Engine.Components;

public sealed class TaxIdComponentType()
    : ComponentTypeBase("taxId", ComponentKind.Input, DataClass.Restricted, ["kinds"], ComponentOperators.Answered);
