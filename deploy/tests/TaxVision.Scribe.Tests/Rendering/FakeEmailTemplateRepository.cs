using BuildingBlocks.Results;
using TaxVision.Scribe.Application.Templates;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.Templates;
using TaxVision.Scribe.Domain.ValueObjects;

namespace TaxVision.Scribe.Tests.Rendering;

internal sealed class FakeEmailTemplateRepository(EmailTemplate template) : IEmailTemplateRepository
{
    public Task<Result<EmailTemplate>> GetByKeyAsync(
        TemplateKey templateKey,
        Guid? tenantId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            template.TemplateKey.Value == templateKey.Value
                ? Result.Success(template)
                : Result.Failure<EmailTemplate>(new Error("EmailTemplate.NotFound", "Not found."))
        );

    public Task<Result<EmailTemplate>> GetByIdAsync(Guid templateId, CancellationToken ct = default) =>
        Task.FromResult(
            template.Id == templateId
                ? Result.Success(template)
                : Result.Failure<EmailTemplate>(new Error("EmailTemplate.NotFound", "Not found."))
        );

    public Task<Result<(EmailTemplate Template, EmailTemplateVersion Version)>> GetVersionByIdAsync(
        Guid versionId,
        CancellationToken ct = default
    )
    {
        var version = template.Versions.FirstOrDefault(v => v.Id == versionId);
        return Task.FromResult(
            version is null
                ? Result.Failure<(EmailTemplate, EmailTemplateVersion)>(
                    new Error("EmailTemplateVersion.NotFound", "Not found.")
                )
                : Result.Success((template, version))
        );
    }

    public Task AddAsync(EmailTemplate value, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<(EmailTemplate Template, EmailTemplateVersion Version)>> GetAllPublishedAsync(
        CancellationToken ct = default
    )
    {
        var published = template
            .Versions.Where(v => v.Status == EmailVersionStatus.Published)
            .Select(v => (template, v))
            .ToList();
        return Task.FromResult<IReadOnlyList<(EmailTemplate, EmailTemplateVersion)>>(published);
    }

    public Task<IReadOnlyList<EmailTemplate>> GetWithArchivedVersionsOlderThanAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken ct = default
    )
    {
        var hasEligibleVersion = template.Versions.Any(v =>
            v.Status == EmailVersionStatus.Archived && v.CreatedAtUtc < cutoffUtc
        );
        return Task.FromResult<IReadOnlyList<EmailTemplate>>(hasEligibleVersion ? [template] : []);
    }
}
