using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing;

namespace TaxVision.Notification.Application.Email.Layouts.Commands;

public sealed record SetDefaultEmailLayoutCommand(Guid LayoutId, Guid? TenantId, bool IsPlatformAdmin);

public static class SetDefaultEmailLayoutHandler
{
    public static async Task<Result> Handle(
        SetDefaultEmailLayoutCommand command,
        IEmailLayoutRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var layout = await repository.GetByIdAsync(command.LayoutId, command.TenantId, ct);
        if (layout is null)
            return Result.Failure(new Error("EmailLayout.NotFound", "Layout not found."));

        if (layout.Scope == EmailScope.System && !command.IsPlatformAdmin)
            return Result.Failure(
                new Error("EmailLayout.Forbidden", "Only platform administrators can manage system layouts.")
            );

        await repository.ClearDefaultsAsync(layout.Scope, layout.TenantId, ct);
        layout.SetAsDefault();
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
