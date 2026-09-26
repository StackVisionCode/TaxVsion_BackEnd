using BuildingBlocks.Results;
using TaxVision.PaymentApp.Application.Abstractions;

namespace TaxVision.PaymentApp.Application.PlanChangeCheckouts.Queries;

public sealed record GetPlanChangePaymentStatusQuery(Guid TenantId, Guid SaaSPaymentId);

public sealed record PlanChangePaymentStatusResponse(string Status, DateTime? PaidAtUtc);

/// <summary>Estado de un pago de upgrade, para el poll de la pantalla. Molde:
/// <c>GetSeatPaymentStatusHandler</c>.</summary>
public static class GetPlanChangePaymentStatusHandler
{
    public static async Task<Result<PlanChangePaymentStatusResponse>> Handle(
        GetPlanChangePaymentStatusQuery query,
        ISaaSPaymentRepository payments,
        CancellationToken ct
    )
    {
        var payment = await payments.GetByIdAsync(query.SaaSPaymentId, query.TenantId, ct);
        if (payment is null)
            return Result.Failure<PlanChangePaymentStatusResponse>(
                new Error("SaaSPayment.NotFound", "The payment does not exist for this tenant.")
            );

        return Result.Success(new PlanChangePaymentStatusResponse(payment.Status.ToString(), payment.PaidAtUtc));
    }
}
