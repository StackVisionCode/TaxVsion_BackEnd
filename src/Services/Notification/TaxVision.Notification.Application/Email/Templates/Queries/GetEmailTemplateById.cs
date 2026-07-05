using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Email.Templates.Queries;

public sealed record GetEmailTemplateByIdQuery(Guid TemplateId, Guid? TenantId);

public static class GetEmailTemplateByIdHandler
{
    public static async Task<Result<EmailTemplateDetailResponse>> Handle(
        GetEmailTemplateByIdQuery query,
        IEmailTemplateRepository repository,
        CancellationToken ct
    )
    {
        var template = await repository.GetByIdAsync(query.TemplateId, query.TenantId, ct);
        if (template is null)
            return Result.Failure<EmailTemplateDetailResponse>(
                new Error("EmailTemplate.NotFound", "Template not found.")
            );

        var versions = await repository.ListVersionsAsync(template.Id, ct);
        IReadOnlyList<EmailTemplateVersionResponse> versionResponses = versions
            .Select(EmailTemplateMapper.ToResponse)
            .ToList();

        return Result.Success(
            new EmailTemplateDetailResponse(EmailTemplateMapper.ToResponse(template), versionResponses)
        );
    }
}
