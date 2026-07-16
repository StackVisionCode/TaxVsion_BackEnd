using BuildingBlocks.Results;
using TaxVision.PaymentApp.Application.Abstractions;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Queries;

public static class GetSaaSPaymentByIdHandler
{
    public static async Task<Result<SaaSPaymentResponse>> Handle(
        GetSaaSPaymentByIdQuery query, ISaaSPaymentRepository payments, CancellationToken ct)
    {
        var payment = await payments.GetByIdAsync(query.SaaSPaymentId, query.TenantId, ct);
        if (payment is null)
            return Result.Failure<SaaSPaymentResponse>(new Error("SaaSPayment.NotFound", "SaaSPayment does not exist."));

        return Result.Success(new SaaSPaymentResponse(
            payment.Id,
            payment.Status.ToString(),
            payment.Type.ToString(),
            payment.Amount.AmountCents,
            payment.Amount.Currency,
            payment.ProviderCode.ToString(),
            payment.ExternalChargeReference?.Value,
            payment.FailureCode,
            payment.FailureReason,
            payment.NextRetryAtUtc,
            payment.PaidAtUtc,
            payment.CreatedAtUtc));
    }
}
