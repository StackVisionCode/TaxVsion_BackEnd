using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing.Sending;

namespace TaxVision.Notification.Application.Email.Sending.Queries;

public sealed record GetOutboundEmailsQuery(Guid TenantId, EmailStatus? Status = null, int Page = 1, int Size = 20);

public static class GetOutboundEmailsHandler
{
    public static async Task<Result<PagedResult<OutboundEmailResponse>>> Handle(
        GetOutboundEmailsQuery query,
        IOutboundEmailRepository repository,
        CancellationToken ct
    )
    {
        if (query.Page < 1 || query.Size is < 1 or > 100)
            return Result.Failure<PagedResult<OutboundEmailResponse>>(
                new Error("Query.Pagination", "Page must be >= 1 and size between 1 and 100.")
            );

        var (items, total) = await repository.GetPagedAsync(query.TenantId, query.Status, query.Page, query.Size, ct);
        IReadOnlyList<OutboundEmailResponse> responses = items.Select(OutboundEmailMapper.ToResponse).ToList();
        return Result.Success(new PagedResult<OutboundEmailResponse>(responses, query.Page, query.Size, total));
    }
}
