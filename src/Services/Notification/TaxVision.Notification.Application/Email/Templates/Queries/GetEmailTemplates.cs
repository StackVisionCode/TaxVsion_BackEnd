using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Email.Templates.Queries;

public sealed record GetEmailTemplatesQuery(Guid? TenantId, bool IncludeSystem = true);

public static class GetEmailTemplatesHandler
{
    public static async Task<Result<IReadOnlyList<EmailTemplateResponse>>> Handle(
        GetEmailTemplatesQuery query,
        IEmailTemplateRepository repository,
        CancellationToken ct
    )
    {
        var items = await repository.ListAsync(query.TenantId, query.IncludeSystem, ct);
        IReadOnlyList<EmailTemplateResponse> responses = items.Select(EmailTemplateMapper.ToResponse).ToList();
        return Result.Success(responses);
    }
}
