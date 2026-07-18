using System.Text;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Scribe.Application.Templates.Storage;
using TaxVision.Scribe.Domain;

namespace TaxVision.Scribe.Application.Templates.Commands;

public sealed record AddEmailTemplateDraftVersionCommand(
    Guid TemplateId,
    Guid? TenantId,
    bool IsPlatformAdmin,
    string Subject,
    string HtmlContent,
    string? TextContent,
    string? DesignJson,
    Guid LayoutId,
    int LayoutVersionNumber,
    IReadOnlyList<VariableDefinitionInput> VariableDefinitions,
    Guid ActorUserId
);

public static class AddEmailTemplateDraftVersionHandler
{
    public static async Task<Result<EmailTemplateVersionResponse>> Handle(
        AddEmailTemplateDraftVersionCommand command,
        IEmailTemplateRepository templateRepository,
        ITemplateStorageService storageService,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var templateResult = await templateRepository.GetByIdAsync(command.TemplateId, ct);
        if (templateResult.IsFailure)
            return Result.Failure<EmailTemplateVersionResponse>(templateResult.Error);

        var template = templateResult.Value;
        var authError = AuthorizeWrite(template.Scope, template.TenantId, command.TenantId, command.IsPlatformAdmin);
        if (authError is not null)
            return Result.Failure<EmailTemplateVersionResponse>(authError);

        var htmlUploadResult = await storageService.UploadAsync(
            template.TenantId,
            TemplateArtifactKind.Html,
            Encoding.UTF8.GetBytes(command.HtmlContent),
            command.ActorUserId,
            ct
        );
        if (htmlUploadResult.IsFailure)
            return Result.Failure<EmailTemplateVersionResponse>(htmlUploadResult.Error);

        string? textStorageKey = null;
        Guid? textFileId = null;
        if (!string.IsNullOrWhiteSpace(command.TextContent))
        {
            var textUploadResult = await storageService.UploadAsync(
                template.TenantId,
                TemplateArtifactKind.Text,
                Encoding.UTF8.GetBytes(command.TextContent),
                command.ActorUserId,
                ct
            );
            if (textUploadResult.IsFailure)
                return Result.Failure<EmailTemplateVersionResponse>(textUploadResult.Error);
            textStorageKey = textUploadResult.Value.StorageKey;
            textFileId = textUploadResult.Value.FileId;
        }

        string? designStorageKey = null;
        Guid? designFileId = null;
        if (!string.IsNullOrWhiteSpace(command.DesignJson))
        {
            var designUploadResult = await storageService.UploadAsync(
                template.TenantId,
                TemplateArtifactKind.DesignJson,
                Encoding.UTF8.GetBytes(command.DesignJson),
                command.ActorUserId,
                ct
            );
            if (designUploadResult.IsFailure)
                return Result.Failure<EmailTemplateVersionResponse>(designUploadResult.Error);
            designStorageKey = designUploadResult.Value.StorageKey;
            designFileId = designUploadResult.Value.FileId;
        }

        var versionResult = template.AddDraftVersion(
            command.Subject,
            htmlUploadResult.Value.StorageKey,
            htmlUploadResult.Value.FileId,
            textStorageKey,
            textFileId,
            designStorageKey,
            designFileId,
            previewImageStorageKey: null,
            previewImageFileId: null,
            command.LayoutId,
            command.LayoutVersionNumber,
            command
                .VariableDefinitions.Select(d => (d.Name, d.Type, d.Required, d.DefaultValue, d.Description))
                .ToList(),
            DateTime.UtcNow
        );
        if (versionResult.IsFailure)
            return Result.Failure<EmailTemplateVersionResponse>(versionResult.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(EmailTemplateMapper.ToVersionResponse(versionResult.Value));
    }

    internal static Error? AuthorizeWrite(
        TemplateScope scope,
        Guid? templateTenantId,
        Guid? callerTenantId,
        bool isPlatformAdmin
    )
    {
        if (scope == TemplateScope.System && !isPlatformAdmin)
            return new Error("EmailTemplate.Forbidden", "Only platform administrators can manage system templates.");

        if (scope == TemplateScope.Tenant && !isPlatformAdmin && templateTenantId != callerTenantId)
            return new Error("EmailTemplate.Forbidden", "This template does not belong to the caller's tenant.");

        return null;
    }
}
