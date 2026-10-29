using System.Text;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Scribe.Application.Templates.Storage;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.Templates;

namespace TaxVision.Scribe.Application.Templates.Commands;

public sealed record AddEmailTemplateDraftVersionCommand(
    Guid TemplateId,
    Guid? TenantId,
    bool IsPlatformAdmin,
    string Subject,
    string? HtmlContent,
    string? TextContent,
    string? DesignJson,
    Guid? LayoutId,
    int? LayoutVersionNumber,
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

        var uploads = await UploadContentAsync(template.Channel, template.TenantId, command, storageService, ct);
        if (uploads.IsFailure)
            return Result.Failure<EmailTemplateVersionResponse>(uploads.Error);

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

        var definitions = command
            .VariableDefinitions.Select(d => (d.Name, d.Type, d.Required, d.DefaultValue, d.Description))
            .ToList();
        var versionResult =
            template.Channel == TemplateChannel.Push
                ? template.AddPushDraftVersion(
                    command.Subject,
                    uploads.Value.TextStorageKey!,
                    uploads.Value.TextFileId!.Value,
                    definitions,
                    DateTime.UtcNow
                )
                : template.AddDraftVersion(
                    command.Subject,
                    uploads.Value.HtmlStorageKey!,
                    uploads.Value.HtmlFileId!.Value,
                    uploads.Value.TextStorageKey,
                    uploads.Value.TextFileId,
                    designStorageKey,
                    designFileId,
                    previewImageStorageKey: null,
                    previewImageFileId: null,
                    command.LayoutId!.Value,
                    command.LayoutVersionNumber!.Value,
                    definitions,
                    DateTime.UtcNow
                );
        if (versionResult.IsFailure)
            return Result.Failure<EmailTemplateVersionResponse>(versionResult.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(EmailTemplateMapper.ToVersionResponse(versionResult.Value));
    }

    private sealed record UploadedContent(
        string? HtmlStorageKey,
        Guid? HtmlFileId,
        string? TextStorageKey,
        Guid? TextFileId
    );

    private static async Task<Result<UploadedContent>> UploadContentAsync(
        TemplateChannel channel,
        Guid? tenantId,
        AddEmailTemplateDraftVersionCommand command,
        ITemplateStorageService storage,
        CancellationToken ct
    )
    {
        if (channel == TemplateChannel.Push)
            return await UploadPushContentAsync(tenantId, command, storage, ct);
        if (
            string.IsNullOrWhiteSpace(command.HtmlContent)
            || command.LayoutId is null
            || command.LayoutVersionNumber is null
        )
            return Result.Failure<UploadedContent>(
                new Error("EmailTemplateVersion.EmailContent", "Email templates require HTML and a layout.")
            );

        var html = await UploadAsync(tenantId, TemplateArtifactKind.Html, command.HtmlContent, command, storage, ct);
        if (html.IsFailure)
            return Result.Failure<UploadedContent>(html.Error);
        var text = await UploadOptionalTextAsync(tenantId, command, storage, ct);
        return text.IsFailure
            ? Result.Failure<UploadedContent>(text.Error)
            : Result.Success(
                new UploadedContent(html.Value.StorageKey, html.Value.FileId, text.Value.Key, text.Value.Id)
            );
    }

    private static async Task<Result<UploadedContent>> UploadPushContentAsync(
        Guid? tenantId,
        AddEmailTemplateDraftVersionCommand command,
        ITemplateStorageService storage,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(command.TextContent))
            return Result.Failure<UploadedContent>(
                new Error("EmailTemplateVersion.TextRequired", "Push templates require text content.")
            );
        var text = await UploadAsync(tenantId, TemplateArtifactKind.Text, command.TextContent, command, storage, ct);
        return text.IsFailure
            ? Result.Failure<UploadedContent>(text.Error)
            : Result.Success(new UploadedContent(null, null, text.Value.StorageKey, text.Value.FileId));
    }

    private static async Task<Result<(string? Key, Guid? Id)>> UploadOptionalTextAsync(
        Guid? tenantId,
        AddEmailTemplateDraftVersionCommand command,
        ITemplateStorageService storage,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(command.TextContent))
            return Result.Success<(string?, Guid?)>((null, null));
        var upload = await UploadAsync(tenantId, TemplateArtifactKind.Text, command.TextContent, command, storage, ct);
        return upload.IsFailure
            ? Result.Failure<(string?, Guid?)>(upload.Error)
            : Result.Success<(string?, Guid?)>((upload.Value.StorageKey, upload.Value.FileId));
    }

    private static Task<Result<TemplateStorageUpload>> UploadAsync(
        Guid? tenantId,
        TemplateArtifactKind kind,
        string content,
        AddEmailTemplateDraftVersionCommand command,
        ITemplateStorageService storage,
        CancellationToken ct
    ) => storage.UploadAsync(tenantId, kind, Encoding.UTF8.GetBytes(content), command.ActorUserId, ct);

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
