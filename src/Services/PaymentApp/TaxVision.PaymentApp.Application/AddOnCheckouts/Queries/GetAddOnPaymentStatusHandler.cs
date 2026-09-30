using BuildingBlocks.Results;
using TaxVision.PaymentApp.Application.Abstractions;

namespace TaxVision.PaymentApp.Application.AddOnCheckouts.Queries;

public sealed record GetAddOnPaymentStatusQuery(Guid TenantId, Guid SaaSPaymentId);

public sealed record AddOnPaymentStatusResponse(string Status, DateTime? PaidAtUtc);

/// <summary>Estado de un pago de add-on para el job de reconciliación de Subscription. Molde:
/// <c>GetSeatPaymentStatusHandler</c>.</summary>
public static class GetAddOnPaymentStatusHandler
{
    public static async Task<Result<AddOnPaymentStatusResponse>> Handle(
        GetAddOnPaymentStatusQuery query,
        ISaaSPaymentRepository payments,
        CancellationToken ct
    )
    {
        var payment = await payments.GetByIdAsync(query.SaaSPaymentId, query.TenantId, ct);
        if (payment is null)
            return Result.Failure<AddOnPaymentStatusResponse>(
                new Error("SaaSPayment.NotFound", "The payment does not exist for this tenant.")
            );

        return Result.Success(new AddOnPaymentStatusResponse(payment.Status.ToString(), payment.PaidAtUtc));
    }
}
