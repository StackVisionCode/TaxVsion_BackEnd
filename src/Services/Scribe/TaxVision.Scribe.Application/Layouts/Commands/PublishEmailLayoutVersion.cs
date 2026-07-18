using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Scribe.Application.Templates.Storage;
using TaxVision.Scribe.Application.Templates.Validation;

namespace TaxVision.Scribe.Application.Layouts.Commands;

public sealed record PublishEmailLayoutVersionCommand(
    Guid LayoutId,
    Guid VersionId,
    Guid? TenantId,
    bool IsPlatformAdmin,
    Guid PublishedByUserId
);

/// <summary>Preflight de seguridad (Fase 4.6/§Scribe_Email_Style_Guide.md) antes de publicar un layout — sin validación de variables: los layouts no declaran VariableDefinitions.</summary>
public static class PublishEmailLayoutVersionHandler
{
    public static async Task<Result<EmailLayoutVersionResponse>> Handle(
        PublishEmailLayoutVersionCommand command,
        IEmailLayoutRepository layoutRepository,
        ITemplateStorageService storageService,
        EmailHtmlSafetyValidator htmlSafetyValidator,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var layoutResult = await layoutRepository.GetByIdAsync(command.LayoutId, ct);
        if (layoutResult.IsFailure)
            return Result.Failure<EmailLayoutVersionResponse>(layoutResult.Error);

        var layout = layoutResult.Value;
        var authError = AddEmailLayoutDraftVersionHandler.AuthorizeWrite(
            layout.Scope,
            layout.TenantId,
            command.TenantId,
            command.IsPlatformAdmin
        );
        if (authError is not null)
            return Result.Failure<EmailLayoutVersionResponse>(authError);

        var version = layout.Versions.FirstOrDefault(v => v.Id == command.VersionId);
        if (version is null)
            return Result.Failure<EmailLayoutVersionResponse>(
                new Error("EmailLayout.VersionNotFound", $"Version {command.VersionId} was not found on this layout.")
            );

        var htmlResult = await storageService.DownloadTextAsync(version.HtmlFileId, layout.TenantId, ct);
        if (htmlResult.IsFailure)
            return Result.Failure<EmailLayoutVersionResponse>(htmlResult.Error);

        var safetyOutcome = htmlSafetyValidator.Validate(htmlResult.Value);
        if (!safetyOutcome.IsAcceptable)
            return Result.Failure<EmailLayoutVersionResponse>(
                new Error(
                    "EmailLayoutVersion.ValidationFailed",
                    string.Join("; ", safetyOutcome.Errors.Select(e => e.Message))
                )
            );

        var publishResult = layout.PublishVersion(command.VersionId, command.PublishedByUserId, DateTime.UtcNow);
        if (publishResult.IsFailure)
            return Result.Failure<EmailLayoutVersionResponse>(publishResult.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(EmailLayoutMapper.ToVersionResponse(version));
    }
}
