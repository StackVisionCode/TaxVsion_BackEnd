using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.Abstractions;

namespace TaxVision.PaymentClient.Application.Recurring.Queries;

public static class SearchTenantRecurringPaymentsHandler
{
    public static async Task<Result<IReadOnlyList<TenantRecurringPaymentResponse>>> Handle(
        SearchTenantRecurringPaymentsQuery query, ITenantRecurringPaymentRepository plans, CancellationToken ct)
    {
        var results = await plans.SearchByTenantAsync(query.TenantId, query.TaxpayerId, query.Status, query.Page, query.PageSize, ct);

        return Result.Success<IReadOnlyList<TenantRecurringPaymentResponse>>(results.Select(GetTenantRecurringPaymentByIdHandler.Map).ToList());
    }
}
