using TaxVision.DataCollection.Engine.Definitions;

namespace TaxVision.DataCollection.Engine.Components;

public sealed class TextareaComponentType()
    : ComponentTypeBase(
        "textarea",
        ComponentKind.Input,
        DataClass.Personal,
        ["maxLength", "rows"],
        ComponentOperators.Answered
    );
