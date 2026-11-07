using BuildingBlocks.Results;
using TaxVision.PaymentApp.Domain.SaaSPayments;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Queries;

/// <summary>
/// GRW-1: Growth pregunta, solo lectura, si el pago ligado a una reserva de código está confirmado. Un evento de
/// transporte no basta para dar por bueno un canje tardío; esta respuesta sale de la fila del pago.
/// <list type="bullet">
/// <item><c>PaymentApp</c>: el pago que lleva esa reserva (<c>CodeReservationId</c> + <c>CodeReservationPaymentId</c>).</item>
/// <item><c>Onboarding</c>: el pago inicial del onboarding (<paramref name="OnboardingId"/>).</item>
/// </list>
/// </summary>
public sealed record GetCodeReservationPaymentOutcomeQuery(
    string PaymentSource,
    Guid PaymentId,
    Guid CodeReservationId,
    Guid? OnboardingId
);

/// <param name="Confirmed">Hay un pago cobrado y no devuelto del todo (<c>Succeeded</c> o <c>PartiallyRefunded</c>).</param>
public sealed record CodeReservationPaymentOutcomeResponse(
    bool Found,
    bool Confirmed,
    string? Status,
    Guid? TenantId,
    string? PromotionSnapshotHash
);

/// <summary>Fila mínima del pago para responder; la implementa Infrastructure sin filtro de tenant.</summary>
public sealed record CodeReservationPaymentRow(Guid TenantId, PaymentStatus Status, string? PromotionSnapshotHash);

public interface ICodeReservationPaymentReader
{
    Task<IReadOnlyList<CodeReservationPaymentRow>> FindByReservationAsync(
        Guid codeReservationId,
        Guid codeReservationPaymentId,
        CancellationToken ct = default
    );

    Task<IReadOnlyList<CodeReservationPaymentRow>> FindOnboardingAsync(
        Guid onboardingId,
        CancellationToken ct = default
    );
}

public static class GetCodeReservationPaymentOutcomeHandler
{
    public const string PaymentAppSource = "PaymentApp";
    public const string OnboardingSource = "Onboarding";

    public static async Task<Result<CodeReservationPaymentOutcomeResponse>> Handle(
        GetCodeReservationPaymentOutcomeQuery query,
        ICodeReservationPaymentReader reader,
        CancellationToken ct
    )
    {
        IReadOnlyList<CodeReservationPaymentRow> rows;
        if (string.Equals(query.PaymentSource, PaymentAppSource, StringComparison.Ordinal))
        {
            if (query.PaymentId == Guid.Empty || query.CodeReservationId == Guid.Empty)
                return Invalid("paymentId and codeReservationId are required.");
            rows = await reader.FindByReservationAsync(query.CodeReservationId, query.PaymentId, ct);
        }
        else if (string.Equals(query.PaymentSource, OnboardingSource, StringComparison.Ordinal))
        {
            if (query.OnboardingId is not { } onboardingId || onboardingId == Guid.Empty)
                return Invalid("onboardingId is required for the Onboarding source.");
            rows = await reader.FindOnboardingAsync(onboardingId, ct);
        }
        else
            return Result.Failure<CodeReservationPaymentOutcomeResponse>(
                new Error("PaymentApp.CodeReservationOutcome.UnsupportedSource", "Unsupported payment source.")
            );

        if (rows.Count == 0)
            return Result.Success(new CodeReservationPaymentOutcomeResponse(false, false, null, null, null));

        var confirmed = rows.FirstOrDefault(row => IsConfirmed(row.Status));
        var chosen = confirmed ?? rows[0];
        return Result.Success(
            new CodeReservationPaymentOutcomeResponse(
                true,
                confirmed is not null,
                chosen.Status.ToString(),
                chosen.TenantId,
                chosen.PromotionSnapshotHash
            )
        );
    }

    // Reembolsado del todo o contracargado: el dinero volvió, el canje no se da por bueno.
    private static bool IsConfirmed(PaymentStatus status) =>
        status is PaymentStatus.Succeeded or PaymentStatus.PartiallyRefunded;

    private static Result<CodeReservationPaymentOutcomeResponse> Invalid(string message) =>
        Result.Failure<CodeReservationPaymentOutcomeResponse>(
            new Error("PaymentApp.CodeReservationOutcome.Invalid", message)
        );
}
