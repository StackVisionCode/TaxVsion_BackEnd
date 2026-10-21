using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Audit;
using TaxVision.Tasks.ClientForms.Domain.Settings;

namespace TaxVision.Tasks.ClientForms.Application.Settings;

public sealed record SetFormModuleAvailabilityCommand(
    Guid TenantId,
    Guid ByUserId,
    bool Enabled,
    string? Note
);

public static class SetFormModuleAvailabilityHandler
{
    public static async Task<Result<FormModuleSettingsResponse>> Handle(
        SetFormModuleAvailabilityCommand command,
        IFormModuleSettingsRepository repository,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var settings = await repository.GetAsync(command.TenantId, ct);
        var changed = settings is null
            ? FormModuleSettings.Create(command.TenantId, command.Enabled, command.Note, command.ByUserId, DateTime.UtcNow)
            : Update(settings, command);
        if (changed.IsFailure)
            return Result.Failure<FormModuleSettingsResponse>(changed.Error);
        if (settings is null)
            repository.Add(changed.Value);
        audit.Record(command.TenantId, null, null, FormAuditAction.ModuleAvailabilityChanged, command.ByUserId);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(FormModuleSettingsResponse.From(changed.Value));
    }

    private static Result<FormModuleSettings> Update(
        FormModuleSettings settings,
        SetFormModuleAvailabilityCommand command
    )
    {
        var result = settings.SetAvailability(command.Enabled, command.Note, command.ByUserId, DateTime.UtcNow);
        return result.IsFailure ? Result.Failure<FormModuleSettings>(result.Error) : Result.Success(settings);
    }
}
