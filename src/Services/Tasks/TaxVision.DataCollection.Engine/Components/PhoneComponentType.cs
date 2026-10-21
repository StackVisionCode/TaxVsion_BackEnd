using TaxVision.DataCollection.Engine.Definitions;

namespace TaxVision.DataCollection.Engine.Components;

public sealed class PhoneComponentType()
    : ComponentTypeBase("phone", ComponentKind.Input, DataClass.Personal, ["defaultCountry"], ComponentOperators.Answered);
