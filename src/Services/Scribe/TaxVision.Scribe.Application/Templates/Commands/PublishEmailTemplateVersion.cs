using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Scribe.Application.Templates.Storage;
using TaxVision.Scribe.Application.Templates.Validation;

namespace TaxVision.Scribe.Application.Templates.Commands;

public sealed record PublishEmailTemplateVersionCommand(
    Guid TemplateId,
    Guid VersionId,
    Guid? TenantId,
    bool IsPlatformAdmin,
    Guid PublishedByUserId
);

/// <summary>Plan §36 Fase 5, ítem 4 — corre Validate automáticamente y rechaza el publish si hay errores.</summary>
public static class PublishEmailTemplateVersionHandler
{
    public static async Task<Result<EmailTemplateVersionResponse>> Handle(
        PublishEmailTemplateVersionCommand command,
        IEmailTemplateRepository templateRepository,
        ITemplateStorageService storageService,
        EmailHtmlSafetyValidator htmlSafetyValidator,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var templateResult = await templateRepository.GetByIdAsync(command.TemplateId, ct);
        if (templateResult.IsFailure)
            return Result.Failure<EmailTemplateVersionResponse>(templateResult.Error);

        var template = templateResult.Value;
        var authError = AddEmailTemplateDraftVersionHandler.AuthorizeWrite(
            template.Scope,
            template.TenantId,
            command.TenantId,
            command.IsPlatformAdmin
        );
        if (authError is not null)
            return Result.Failure<EmailTemplateVersionResponse>(authError);

        var version = template.Versions.FirstOrDefault(v => v.Id == command.VersionId);
        if (version is null)
            return Result.Failure<EmailTemplateVersionResponse>(
                new Error(
                    "EmailTemplate.VersionNotFound",
                    $"Version {command.VersionId} was not found on this template."
                )
            );

        var validationResult = await TemplateVersionValidator.ValidateAsync(
            version,
            template.TenantId,
            storageService,
            htmlSafetyValidator,
            ct
        );
        if (validationResult.IsFailure)
            return Result.Failure<EmailTemplateVersionResponse>(validationResult.Error);

        if (!validationResult.Value.IsValid)
            return Result.Failure<EmailTemplateVersionResponse>(
                new Error(
                    "EmailTemplateVersion.ValidationFailed",
                    string.Join("; ", validationResult.Value.Errors.Select(e => e.Message))
                )
            );

        var publishResult = template.PublishVersion(command.VersionId, command.PublishedByUserId, DateTime.UtcNow);
        if (publishResult.IsFailure)
            return Result.Failure<EmailTemplateVersionResponse>(publishResult.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(EmailTemplateMapper.ToVersionResponse(version));
    }
}
