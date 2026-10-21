using TaxVision.DataCollection.Engine.Definitions;

namespace TaxVision.DataCollection.Engine.Components;

public sealed class CheckboxComponentType()
    : ComponentTypeBase("checkbox", ComponentKind.Input, DataClass.General, ["text"], ComponentOperators.Checkbox);
