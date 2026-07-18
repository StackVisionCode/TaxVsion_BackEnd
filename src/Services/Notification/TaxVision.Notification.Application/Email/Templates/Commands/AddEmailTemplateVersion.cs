using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing;
using TaxVision.Notification.Domain.Emailing.Templates;

namespace TaxVision.Notification.Application.Email.Templates.Commands;

/// <summary>
/// Añade una nueva versión (Draft) a una plantilla: sube HTML/design/preview a CloudStorage y guarda
/// las claves+FileIds. El almacenamiento ocurre antes de persistir en BD.
/// </summary>
public sealed record AddEmailTemplateVersionCommand(
    Guid TemplateId,
    Guid? TenantId,
    bool IsPlatformAdmin,
    Guid? CreatedByUserId,
    string SubjectTemplate,
    string Html,
    string? DesignJson,
    byte[]? PreviewPng
);

public static class AddEmailTemplateVersionHandler
{
    public static async Task<Result<EmailTemplateVersionResponse>> Handle(
        AddEmailTemplateVersionCommand command,
        IEmailTemplateRepository repository,
        ITemplateStorageService storage,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var template = await repository.GetByIdAsync(command.TemplateId, command.TenantId, ct);
        if (template is null)
            return Result.Failure<EmailTemplateVersionResponse>(
                new Error("EmailTemplate.NotFound", "Template not found.")
            );

        if (template.Scope == EmailScope.System && !command.IsPlatformAdmin)
            return Result.Failure<EmailTemplateVersionResponse>(
                new Error("EmailTemplate.Forbidden", "Only platform administrators can manage system templates.")
            );

        if (template.Status == EmailTemplateStatus.Archived)
            return Result.Failure<EmailTemplateVersionResponse>(
                new Error("EmailTemplate.Archived", "An archived template cannot receive new versions.")
            );

        if (string.IsNullOrWhiteSpace(command.Html))
            return Result.Failure<EmailTemplateVersionResponse>(
                new Error("EmailTemplate.Html", "HTML body is required.")
            );

        var versionNumber = await repository.GetNextVersionNumberAsync(template.Id, ct);

        var stored = await storage.StoreVersionAsync(
            template.Scope,
            template.TenantId,
            template.TemplateKey,
            versionNumber,
            command.Html,
            command.DesignJson,
            command.PreviewPng,
            ct
        );
        if (stored.IsFailure)
            return Result.Failure<EmailTemplateVersionResponse>(stored.Error);

        var refs = stored.Value;
        var versionResult = EmailTemplateVersion.Create(
            template.Id,
            versionNumber,
            command.SubjectTemplate,
            refs.HtmlKey,
            refs.HtmlFileId,
            refs.DesignKey,
            refs.DesignFileId,
            refs.PreviewKey,
            refs.PreviewFileId,
            command.CreatedByUserId
        );
        if (versionResult.IsFailure)
            return Result.Failure<EmailTemplateVersionResponse>(versionResult.Error);

        await repository.AddVersionAsync(versionResult.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(EmailTemplateMapper.ToResponse(versionResult.Value));
    }
}
