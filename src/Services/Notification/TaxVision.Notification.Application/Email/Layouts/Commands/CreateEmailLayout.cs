using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing;
using TaxVision.Notification.Domain.Emailing.Layouts;

namespace TaxVision.Notification.Application.Email.Layouts.Commands;

public sealed record CreateEmailLayoutCommand(
    EmailScope Scope,
    Guid? TenantId,
    bool IsPlatformAdmin,
    Guid? CreatedByUserId,
    string LayoutName,
    string Html,
    string? DesignJson,
    byte[]? PreviewPng,
    bool IsDefault
);

public static class CreateEmailLayoutHandler
{
    public static async Task<Result<EmailLayoutResponse>> Handle(
        CreateEmailLayoutCommand command,
        IEmailLayoutRepository repository,
        ILayoutStorageService storage,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (command.Scope == EmailScope.System && !command.IsPlatformAdmin)
            return Result.Failure<EmailLayoutResponse>(
                new Error("EmailLayout.Forbidden", "Only platform administrators can manage system layouts.")
            );

        if (command.Scope == EmailScope.Tenant && (command.TenantId is null || command.TenantId == Guid.Empty))
            return Result.Failure<EmailLayoutResponse>(
                new Error("EmailLayout.Tenant", "A tenant context is required for tenant layouts.")
            );

        if (string.IsNullOrWhiteSpace(command.Html))
            return Result.Failure<EmailLayoutResponse>(new Error("EmailLayout.Html", "Layout HTML is required."));

        var stored = await storage.StoreAsync(
            command.Scope,
            command.TenantId,
            command.LayoutName,
            command.Html,
            command.DesignJson,
            command.PreviewPng,
            ct
        );
        if (stored.IsFailure)
            return Result.Failure<EmailLayoutResponse>(stored.Error);

        var refs = stored.Value;
        var result = EmailLayout.Create(
            command.Scope,
            command.TenantId,
            command.LayoutName,
            refs.HtmlKey,
            refs.HtmlFileId,
            refs.DesignKey,
            refs.DesignFileId,
            refs.PreviewKey,
            refs.PreviewFileId,
            command.IsDefault,
            command.CreatedByUserId
        );
        if (result.IsFailure)
            return Result.Failure<EmailLayoutResponse>(result.Error);

        var layout = result.Value;
        if (layout.IsDefault)
            await repository.ClearDefaultsAsync(layout.Scope, layout.TenantId, ct);

        await repository.AddAsync(layout, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(EmailLayoutMapper.ToResponse(layout));
    }
}
