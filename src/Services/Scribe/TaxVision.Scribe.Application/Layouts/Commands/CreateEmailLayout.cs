using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.Layouts;
using TaxVision.Scribe.Domain.ValueObjects;

namespace TaxVision.Scribe.Application.Layouts.Commands;

public sealed record CreateEmailLayoutCommand(
    TemplateScope Scope,
    Guid? TenantId,
    bool IsPlatformAdmin,
    string LayoutKey,
    string Name,
    string? Description,
    Guid CreatedByUserId
);

public static class CreateEmailLayoutHandler
{
    public static async Task<Result<EmailLayoutResponse>> Handle(
        CreateEmailLayoutCommand command,
        IEmailLayoutRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (command.Scope == TemplateScope.System && !command.IsPlatformAdmin)
            return Result.Failure<EmailLayoutResponse>(
                new Error("EmailLayout.Forbidden", "Only platform administrators can manage system layouts.")
            );

        if (command.Scope == TemplateScope.Tenant && (command.TenantId is null || command.TenantId == Guid.Empty))
            return Result.Failure<EmailLayoutResponse>(
                new Error("EmailLayout.Tenant", "A tenant context is required for tenant layouts.")
            );

        var layoutKeyResult = LayoutKey.Create(command.LayoutKey);
        if (layoutKeyResult.IsFailure)
            return Result.Failure<EmailLayoutResponse>(layoutKeyResult.Error);

        var layoutResult = EmailLayout.CreateNew(
            command.Scope,
            command.Scope == TemplateScope.Tenant ? command.TenantId : null,
            layoutKeyResult.Value,
            command.Name,
            command.Description,
            command.CreatedByUserId,
            DateTime.UtcNow
        );
        if (layoutResult.IsFailure)
            return Result.Failure<EmailLayoutResponse>(layoutResult.Error);

        await repository.AddAsync(layoutResult.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(EmailLayoutMapper.ToResponse(layoutResult.Value));
    }
}
