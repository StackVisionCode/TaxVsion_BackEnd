using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Campaigns.Application.Senders.Abstractions;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Senders;

namespace TaxVision.Campaigns.Application.Senders.Queries;

public sealed record ListSenderProfilesQuery(Guid TenantId, CampaignChannel? Channel, int Page, int Size);

public static class ListSenderProfilesHandler
{
    public static async Task<PagedResult<SenderProfileResponse>> Handle(
        ListSenderProfilesQuery query,
        ISenderProfileRepository senders,
        CancellationToken ct
    )
    {
        var page = await senders.ListAsync(query.TenantId, query.Channel, query.Page, query.Size, ct);
        return new PagedResult<SenderProfileResponse>(
            page.Items.Select(SenderProfileResponse.From).ToList(),
            page.Page,
            page.Size,
            page.TotalCount
        );
    }
}

public sealed record GetSenderProfileQuery(Guid TenantId, Guid Id);

public static class GetSenderProfileHandler
{
    public static async Task<Result<SenderProfileResponse>> Handle(
        GetSenderProfileQuery query,
        ISenderProfileRepository senders,
        CancellationToken ct
    )
    {
        var sender = await senders.GetByIdAsync(query.TenantId, query.Id, ct);
        return sender is null
            ? Result.Failure<SenderProfileResponse>(SenderProfileErrors.NotFound)
            : Result.Success(SenderProfileResponse.From(sender));
    }
}
