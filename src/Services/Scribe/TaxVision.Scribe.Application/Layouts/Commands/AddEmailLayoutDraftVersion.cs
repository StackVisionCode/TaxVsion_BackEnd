using System.Text;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Scribe.Application.Templates.Storage;
using TaxVision.Scribe.Domain;

namespace TaxVision.Scribe.Application.Layouts.Commands;

public sealed record AddEmailLayoutDraftVersionCommand(
    Guid LayoutId,
    Guid? TenantId,
    bool IsPlatformAdmin,
    string HtmlContent,
    string? DesignJson,
    Guid ActorUserId
);

public static class AddEmailLayoutDraftVersionHandler
{
    public static async Task<Result<EmailLayoutVersionResponse>> Handle(
        AddEmailLayoutDraftVersionCommand command,
        IEmailLayoutRepository layoutRepository,
        ITemplateStorageService storageService,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var layoutResult = await layoutRepository.GetByIdAsync(command.LayoutId, ct);
        if (layoutResult.IsFailure)
            return Result.Failure<EmailLayoutVersionResponse>(layoutResult.Error);

        var layout = layoutResult.Value;
        var authError = AuthorizeWrite(layout.Scope, layout.TenantId, command.TenantId, command.IsPlatformAdmin);
        if (authError is not null)
            return Result.Failure<EmailLayoutVersionResponse>(authError);

        var htmlUploadResult = await storageService.UploadAsync(
            layout.TenantId,
            TemplateArtifactKind.Html,
            Encoding.UTF8.GetBytes(command.HtmlContent),
            command.ActorUserId,
            ct
        );
        if (htmlUploadResult.IsFailure)
            return Result.Failure<EmailLayoutVersionResponse>(htmlUploadResult.Error);

        string? designStorageKey = null;
        Guid? designFileId = null;
        if (!string.IsNullOrWhiteSpace(command.DesignJson))
        {
            var designUploadResult = await storageService.UploadAsync(
                layout.TenantId,
                TemplateArtifactKind.DesignJson,
                Encoding.UTF8.GetBytes(command.DesignJson),
                command.ActorUserId,
                ct
            );
            if (designUploadResult.IsFailure)
                return Result.Failure<EmailLayoutVersionResponse>(designUploadResult.Error);
            designStorageKey = designUploadResult.Value.StorageKey;
            designFileId = designUploadResult.Value.FileId;
        }

        var versionResult = layout.AddDraftVersion(
            htmlUploadResult.Value.StorageKey,
            htmlUploadResult.Value.FileId,
            designStorageKey,
            designFileId,
            previewImageStorageKey: null,
            previewImageFileId: null,
            DateTime.UtcNow
        );
        if (versionResult.IsFailure)
            return Result.Failure<EmailLayoutVersionResponse>(versionResult.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(EmailLayoutMapper.ToVersionResponse(versionResult.Value));
    }

    internal static Error? AuthorizeWrite(
        TemplateScope scope,
        Guid? layoutTenantId,
        Guid? callerTenantId,
        bool isPlatformAdmin
    )
    {
        if (scope == TemplateScope.System && !isPlatformAdmin)
            return new Error("EmailLayout.Forbidden", "Only platform administrators can manage system layouts.");

        if (scope == TemplateScope.Tenant && !isPlatformAdmin && layoutTenantId != callerTenantId)
            return new Error("EmailLayout.Forbidden", "This layout does not belong to the caller's tenant.");

        return null;
    }
}
