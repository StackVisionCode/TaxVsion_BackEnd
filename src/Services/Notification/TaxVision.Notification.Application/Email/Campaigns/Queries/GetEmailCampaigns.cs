using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing.Campaigns;

namespace TaxVision.Notification.Application.Email.Campaigns.Queries;

public sealed record GetEmailCampaignsQuery(Guid TenantId, CampaignStatus? Status = null, int Page = 1, int Size = 20);

public static class GetEmailCampaignsHandler
{
    public static async Task<Result<PagedResult<EmailCampaignResponse>>> Handle(
        GetEmailCampaignsQuery query,
        IEmailCampaignRepository repository,
        CancellationToken ct
    )
    {
        if (query.Page < 1 || query.Size is < 1 or > 100)
            return Result.Failure<PagedResult<EmailCampaignResponse>>(
                new Error("Query.Pagination", "Page must be >= 1 and size between 1 and 100.")
            );

        var (items, total) = await repository.GetPagedAsync(query.TenantId, query.Status, query.Page, query.Size, ct);
        IReadOnlyList<EmailCampaignResponse> responses = items.Select(EmailCampaignMapper.ToResponse).ToList();
        return Result.Success(new PagedResult<EmailCampaignResponse>(responses, query.Page, query.Size, total));
    }
}
