namespace TaxVision.DataCollection.Engine.Components;

public sealed class ConsentComponentType()
    : ComponentTypeBase(
        "consent",
        ComponentKind.Compliance,
        null,
        ["noticeKey", "noticeVersionNumber", "mode", "timing", "acceptanceText", "blocking"]
    );
