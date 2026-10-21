using TaxVision.DataCollection.Engine.Definitions;

namespace TaxVision.DataCollection.Engine.Components;

public sealed class TextComponentType()
    : ComponentTypeBase(
        "text",
        ComponentKind.Input,
        DataClass.Personal,
        ["maxLength", "minLength", "autocomplete"],
        ComponentOperators.Equality
    );
