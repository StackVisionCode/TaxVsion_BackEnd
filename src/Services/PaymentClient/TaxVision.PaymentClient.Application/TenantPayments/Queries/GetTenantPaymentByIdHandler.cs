using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.Abstractions;

namespace TaxVision.PaymentClient.Application.TenantPayments.Queries;

public static class GetTenantPaymentByIdHandler
{
    public static async Task<Result<TenantPaymentResponse>> Handle(
        GetTenantPaymentByIdQuery query,
        ITenantPaymentRepository payments,
        CancellationToken ct
    )
    {
        var payment = await payments.GetByIdAsync(query.TenantPaymentId, query.TenantId, ct);
        if (payment is null)
            return Result.Failure<TenantPaymentResponse>(
                new Error("TenantPayment.NotFound", "TenantPayment does not exist.")
            );

        return Result.Success(
            new TenantPaymentResponse(
                payment.Id,
                payment.Amount.AmountCents,
                payment.Amount.Currency,
                payment.TaxpayerId,
                payment.Purpose.Kind.ToString(),
                payment.Purpose.ExternalReferenceId,
                payment.ProviderCode.ToString(),
                payment.Status.ToString(),
                payment.ExternalChargeReference?.Value,
                payment.ProviderChargeReferenceOnConnect,
                payment.SplitPayment?.TenantAmountCents,
                payment.SplitPayment?.PlatformFeeAmountCents,
                payment.NextActionType,
                payment.NextActionUrl,
                payment.FailureCode,
                payment.FailureReason,
                payment.PaidAtUtc
            )
        );
    }
}
