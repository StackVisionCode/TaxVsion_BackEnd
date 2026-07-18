using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Domain.TenantPayments;

namespace TaxVision.PaymentClient.Application.Admin.Queries;

public static class SearchTenantPaymentsAdminHandler
{
    public static async Task<Result<IReadOnlyList<TenantPaymentAdminResponse>>> Handle(
        SearchTenantPaymentsAdminQuery query,
        ITenantPaymentRepository payments,
        CancellationToken ct
    )
    {
        var results = await payments.SearchAdminAsync(
            query.TenantId,
            query.Status,
            query.From,
            query.To,
            query.Page,
            query.PageSize,
            ct
        );

        return Result.Success<IReadOnlyList<TenantPaymentAdminResponse>>(results.Select(Map).ToList());
    }

    private static TenantPaymentAdminResponse Map(TenantPayment payment) =>
        new(
            payment.Id,
            payment.TenantId,
            payment.Amount.AmountCents,
            payment.Amount.Currency,
            payment.TaxpayerId,
            payment.Purpose.Kind.ToString(),
            payment.Purpose.ExternalReferenceId,
            payment.ProviderCode.ToString(),
            payment.Status.ToString(),
            payment.ExternalChargeReference?.Value,
            payment.FailureCode,
            payment.FailureReason,
            payment.PaidAtUtc,
            payment.CreatedAtUtc
        );
}
