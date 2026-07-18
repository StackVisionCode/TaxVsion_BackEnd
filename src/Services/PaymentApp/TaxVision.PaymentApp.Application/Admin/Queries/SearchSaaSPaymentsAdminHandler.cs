using BuildingBlocks.Results;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Domain.SaaSPayments;

namespace TaxVision.PaymentApp.Application.Admin.Queries;

public static class SearchSaaSPaymentsAdminHandler
{
    public static async Task<Result<IReadOnlyList<SaaSPaymentAdminResponse>>> Handle(
        SearchSaaSPaymentsAdminQuery query, ISaaSPaymentRepository payments, CancellationToken ct)
    {
        var results = await payments.SearchAdminAsync(
            query.TenantId, query.Status, query.Type, query.From, query.To, query.Page, query.PageSize, ct);

        return Result.Success<IReadOnlyList<SaaSPaymentAdminResponse>>(results.Select(Map).ToList());
    }

    private static SaaSPaymentAdminResponse Map(SaaSPayment payment) => new(
        payment.Id,
        payment.TenantId,
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
        payment.CreatedAtUtc);
}
