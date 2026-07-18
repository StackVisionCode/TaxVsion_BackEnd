using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.Templates;
using TaxVision.Scribe.Domain.ValueObjects;

namespace TaxVision.Scribe.Application.Templates.Commands;

public sealed record CreateEmailTemplateCommand(
    TemplateScope Scope,
    Guid? TenantId,
    bool IsPlatformAdmin,
    string TemplateKey,
    string Name,
    string? Description,
    Guid CreatedByUserId
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
        if (command.Scope == TemplateScope.System && !command.IsPlatformAdmin)
            return Result.Failure<EmailTemplateResponse>(
                new Error("EmailTemplate.Forbidden", "Only platform administrators can manage system templates.")
            );

        if (command.Scope == TemplateScope.Tenant && (command.TenantId is null || command.TenantId == Guid.Empty))
            return Result.Failure<EmailTemplateResponse>(
                new Error("EmailTemplate.Tenant", "A tenant context is required for tenant templates.")
            );

        var templateKeyResult = TemplateKey.Create(command.TemplateKey);
        if (templateKeyResult.IsFailure)
            return Result.Failure<EmailTemplateResponse>(templateKeyResult.Error);

        var templateResult = EmailTemplate.CreateNew(
            command.Scope,
            command.Scope == TemplateScope.Tenant ? command.TenantId : null,
            templateKeyResult.Value,
            command.Name,
            command.Description,
            command.CreatedByUserId,
            DateTime.UtcNow
        );
        if (templateResult.IsFailure)
            return Result.Failure<EmailTemplateResponse>(templateResult.Error);

        await repository.AddAsync(templateResult.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(EmailTemplateMapper.ToResponse(templateResult.Value));
    }
}
