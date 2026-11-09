using BuildingBlocks.Results;
using TaxVision.Codes.Application.Reservations.CommitReservation;

namespace TaxVision.Growth.Api.IntegrationEvents;

/// <summary>
/// Qué fallos de los consumidores de pago son reglas fijas: reintentar no cambia nada y van a la cola de errores.
/// Lo que no está en la lista se trata como transitorio y se reintenta.
/// </summary>
public static class GrowthErrorClassification
{
    public const string InvalidPaymentSourceCode = "Growth.PaymentEvent.InvalidSource";
    public const string SnapshotRequiredCode = "Growth.PaymentEvent.SnapshotRequired";

    private static readonly HashSet<string> BusinessRuleCodes =
    [
        InvalidPaymentSourceCode,
        SnapshotRequiredCode,
        CommitReservationHandler.LateCommitPendingReviewCode,
        "Codes.CommitReservation.PaymentMismatch",
        "Codes.CommitReservation.SnapshotMismatch",
        "Codes.CommitReservation.CodeNotFound",
        "Codes.CancelReservation.PaymentMismatch",
        "Codes.CancelReservation.CodeNotFound",
    ];

    public static bool IsBusinessRule(string errorCode) => BusinessRuleCodes.Contains(errorCode);

    public static Exception ToException(Error error) => ToException(error.Code, error.Message);

    public static Exception ToException(string errorCode, string message) =>
        IsBusinessRule(errorCode)
            ? new GrowthBusinessRuleException(errorCode, message)
            : new GrowthFinancialEventException(errorCode, message);
}
