using TaxVision.DataCollection.Engine.Definitions;

namespace TaxVision.DataCollection.Engine.Components;

public sealed class FileComponentType()
    : ComponentTypeBase(
        "file",
        ComponentKind.Input,
        DataClass.Personal,
        ["maxFiles", "allowedTypes", "maxFileBytes", "instructions"],
        ComponentOperators.Answered
    );
