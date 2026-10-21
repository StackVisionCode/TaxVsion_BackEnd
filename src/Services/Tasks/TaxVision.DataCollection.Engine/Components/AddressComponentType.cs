using TaxVision.DataCollection.Engine.Definitions;

namespace TaxVision.DataCollection.Engine.Components;

public sealed class AddressComponentType()
    : ComponentTypeBase("address", ComponentKind.Input, DataClass.Personal, ["country"], ComponentOperators.Answered);
