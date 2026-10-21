namespace TaxVision.DataCollection.Engine.Components;

public sealed class SignatureStepComponentType()
    : ComponentTypeBase(
        "signatureStep",
        ComponentKind.Compliance,
        null,
        ["documentSource", "verification", "instructions"]
    );
