using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing;
using TaxVision.Notification.Domain.Emailing.Templates;

namespace TaxVision.Notification.Application.Email.Templates.Commands;

public sealed record CreateEmailTemplateCommand(
    EmailScope Scope,
    Guid? TenantId,
    bool IsPlatformAdmin,
    Guid? CreatedByUserId,
    string TemplateKey,
    string Subject,
    string? Description,
    string? Category,
    IReadOnlyList<string>? Variables
);

public static class CreateEmailTemplateHandler
{
    public static async Task<Result<EmailTemplateResponse>> Handle(
        CreateEmailTemplateCommand command,
        IEmailTemplateRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (command.Scope == EmailScope.System && !command.IsPlatformAdmin)
            return Result.Failure<EmailTemplateResponse>(
                new Error("EmailTemplate.Forbidden", "Only platform administrators can manage system templates.")
            );

        if (command.Scope == EmailScope.Tenant && (command.TenantId is null || command.TenantId == Guid.Empty))
            return Result.Failure<EmailTemplateResponse>(
                new Error("EmailTemplate.Tenant", "A tenant context is required for tenant templates.")
            );

        var existing = await repository.GetByKeyAsync(command.Scope, command.TenantId, command.TemplateKey, ct);
        if (existing is not null)
            return Result.Failure<EmailTemplateResponse>(
                new Error("EmailTemplate.KeyConflict", "A template with the same key already exists in this scope.")
            );

        var result = EmailTemplate.Create(
            command.Scope,
            command.TenantId,
            command.TemplateKey,
            command.Subject,
            command.Description,
            command.Category,
            EmailTemplateMapper.SerializeVariables(command.Variables),
            command.CreatedByUserId
        );
        if (result.IsFailure)
            return Result.Failure<EmailTemplateResponse>(result.Error);

        await repository.AddAsync(result.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(EmailTemplateMapper.ToResponse(result.Value));
    }
}
