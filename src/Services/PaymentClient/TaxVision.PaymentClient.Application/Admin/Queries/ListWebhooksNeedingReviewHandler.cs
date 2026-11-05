using BuildingBlocks.Results;

namespace TaxVision.PaymentClient.Application.Admin.Queries;

public static class ListWebhooksNeedingReviewHandler
{
    public static async Task<Result<IReadOnlyList<WebhookNeedingReviewResponse>>> Handle(
        ListWebhooksNeedingReviewQuery query,
        IWebhookReviewReader reader,
        CancellationToken ct
    ) => Result.Success(await reader.ListNeedingReviewAsync(ct));
}
