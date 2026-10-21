using TaxVision.DataCollection.Engine.Definitions;

namespace TaxVision.DataCollection.Engine.Components;

public sealed class EmailComponentType()
    : ComponentTypeBase("email", ComponentKind.Input, DataClass.Personal, null, ComponentOperators.Equality);
