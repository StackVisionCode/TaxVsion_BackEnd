using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Email.Layouts.Queries;

public sealed record GetEmailLayoutsQuery(Guid? TenantId, bool IncludeSystem = true);

public static class GetEmailLayoutsHandler
{
    public static async Task<Result<IReadOnlyList<EmailLayoutResponse>>> Handle(
        GetEmailLayoutsQuery query,
        IEmailLayoutRepository repository,
        CancellationToken ct
    )
    {
        var items = await repository.ListAsync(query.TenantId, query.IncludeSystem, ct);
        IReadOnlyList<EmailLayoutResponse> responses = items.Select(EmailLayoutMapper.ToResponse).ToList();
        return Result.Success(responses);
    }
}
