namespace TaxVision.Postmaster.Application.Feedback.Commands.ApplyPolledDeliveryFeedback;

/// <summary>Lo que devolvió la consulta al proveedor, ya traducido. Va en línea, sin pasar por una cola.</summary>
public sealed record ApplyPolledDeliveryFeedbackCommand(
    IReadOnlyList<TranslatedFeedback> Items,
    DateTime ReceivedAtUtc
);
