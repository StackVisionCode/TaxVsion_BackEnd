using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing;

namespace TaxVision.Notification.Application.Email.Templates.Commands;

/// <summary>Publica una versión concreta como la activa; supersede la anterior. Idempotente por versión.</summary>
public sealed record PublishEmailTemplateCommand(Guid TemplateId, Guid VersionId, Guid? TenantId, bool IsPlatformAdmin);

public static class PublishEmailTemplateHandler
{
    public static async Task<Result> Handle(
        PublishEmailTemplateCommand command,
        IEmailTemplateRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var template = await repository.GetByIdAsync(command.TemplateId, command.TenantId, ct);
        if (template is null)
            return Result.Failure(new Error("EmailTemplate.NotFound", "Template not found."));

        if (template.Scope == EmailScope.System && !command.IsPlatformAdmin)
            return Result.Failure(
                new Error("EmailTemplate.Forbidden", "Only platform administrators can manage system templates.")
            );

        var target = await repository.GetVersionAsync(template.Id, command.VersionId, ct);
        if (target is null)
            return Result.Failure(new Error("EmailTemplate.NotFound", "Template version not found."));

        var current = template.CurrentVersionId is { } currentId
            ? await repository.GetVersionAsync(template.Id, currentId, ct)
            : null;

        // Wolverine guarda al salir del handler aunque devuelva error: nada se muta antes de la regla.
        var published = template.Publish(target, current);
        if (published.IsFailure)
            return published;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
