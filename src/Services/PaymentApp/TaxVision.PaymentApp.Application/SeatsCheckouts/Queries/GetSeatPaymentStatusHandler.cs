using BuildingBlocks.Results;
using TaxVision.PaymentApp.Application.Abstractions;

namespace TaxVision.PaymentApp.Application.SeatsCheckouts.Queries;

/// <summary>Estado de un <c>SaaSPayment</c> de compra de asientos — lo consulta Subscription (job de
/// reconciliación) para cerrar intenciones que quedaron <c>Pending</c> pese a que el pago ya se confirmó,
/// cuando el evento de resultado se perdió. M2M-only.</summary>
public sealed record GetSeatPaymentStatusQuery(Guid TenantId, Guid SaaSPaymentId);

public sealed record SeatPaymentStatusResponse(string Status, DateTime? PaidAtUtc);

public static class GetSeatPaymentStatusHandler
{
    public static async Task<Result<SeatPaymentStatusResponse>> Handle(
        GetSeatPaymentStatusQuery query,
        ISaaSPaymentRepository payments,
        CancellationToken ct
    )
    {
        var payment = await payments.GetByIdAsync(query.SaaSPaymentId, query.TenantId, ct);
        if (payment is null)
            return Result.Failure<SeatPaymentStatusResponse>(
                new Error("SaaSPayment.NotFound", "The payment does not exist for this tenant.")
            );

        return Result.Success(new SeatPaymentStatusResponse(payment.Status.ToString(), payment.PaidAtUtc));
    }
}
