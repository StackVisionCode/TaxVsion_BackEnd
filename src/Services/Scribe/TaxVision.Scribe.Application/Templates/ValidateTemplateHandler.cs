using BuildingBlocks.Results;
using TaxVision.Scribe.Application.Templates.Storage;
using TaxVision.Scribe.Application.Templates.Validation;

namespace TaxVision.Scribe.Application.Templates;

public sealed record ValidateTemplateVersionQuery(Guid VersionId, Guid? TenantId, bool IsPlatformAdmin);

/// <summary>Plan §36 Fase 5, ítem 2 — parsea/analiza una versión y reporta placeholders no declarados + issues de EmailHtmlSafetyValidator, sin publicarla.</summary>
public static class ValidateTemplateHandler
{
    public static async Task<Result<TemplateValidationOutcome>> Handle(
        ValidateTemplateVersionQuery query,
        IEmailTemplateRepository templateRepository,
        ITemplateStorageService storageService,
        EmailHtmlSafetyValidator htmlSafetyValidator,
        CancellationToken ct
    )
    {
        var versionResult = await templateRepository.GetVersionByIdAsync(query.VersionId, ct);
        if (versionResult.IsFailure)
            return Result.Failure<TemplateValidationOutcome>(versionResult.Error);

        var (template, version) = versionResult.Value;
        if (template.TenantId is not null && template.TenantId != query.TenantId && !query.IsPlatformAdmin)
            return Result.Failure<TemplateValidationOutcome>(
                new Error("EmailTemplateVersion.NotFound", $"Version {query.VersionId} was not found.")
            );

        return await TemplateVersionValidator.ValidateAsync(
            version,
            template.TenantId,
            storageService,
            htmlSafetyValidator,
            ct
        );
    }
}
