using BuildingBlocks.Results;
using TaxVision.PaymentApp.Application.Abstractions;

namespace TaxVision.PaymentApp.Application.SubscriptionRenewalCheckouts.Queries;

/// <summary>Estado de un <c>SaaSPayment</c> de renovación self-service — lo consulta Subscription (job de
/// reconciliación) para cerrar intenciones que quedaron <c>Pending</c> pese a que el pago ya se confirmó,
/// cuando el evento de resultado se perdió. M2M-only. Molde: <c>GetSeatPaymentStatusHandler</c>.</summary>
public sealed record GetSubscriptionRenewalPaymentStatusQuery(Guid TenantId, Guid SaaSPaymentId);

public sealed record SubscriptionRenewalPaymentStatusResponse(string Status, DateTime? PaidAtUtc);

public static class GetSubscriptionRenewalPaymentStatusHandler
{
    public static async Task<Result<SubscriptionRenewalPaymentStatusResponse>> Handle(
        GetSubscriptionRenewalPaymentStatusQuery query,
        ISaaSPaymentRepository payments,
        CancellationToken ct
    )
    {
        var payment = await payments.GetByIdAsync(query.SaaSPaymentId, query.TenantId, ct);
        if (payment is null)
            return Result.Failure<SubscriptionRenewalPaymentStatusResponse>(
                new Error("SaaSPayment.NotFound", "The payment does not exist for this tenant.")
            );

        return Result.Success(
            new SubscriptionRenewalPaymentStatusResponse(payment.Status.ToString(), payment.PaidAtUtc)
        );
    }
}
