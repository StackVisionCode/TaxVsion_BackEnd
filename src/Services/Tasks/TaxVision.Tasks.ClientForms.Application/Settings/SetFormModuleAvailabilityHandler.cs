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
    string? Note,
    bool? ExternalParticipantsEnabled = null,
    bool? IntakeLinksEnabled = null,
    bool? AutoCustomerCreationEnabled = null,
    bool? SignatureStepEnabled = null,
    bool? PushNotificationsEnabled = null
);

public static class SetFormModuleAvailabilityHandler
{
    public static async Task<Result<FormModuleSettingsResponse>> Handle(
        SetFormModuleAvailabilityCommand command,
        IFormModuleSettingsRepository repository,
        IFormAuditLog audit,
        IFormIntakeBotProtection botProtection,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (command.IntakeLinksEnabled == true && !botProtection.IsReady)
            return Result.Failure<FormModuleSettingsResponse>(FormModuleSettingsErrors.IntakeLinksNotReady);
        var settings = await repository.GetAsync(command.TenantId, ct);
        var changed = settings is null
            ? FormModuleSettings.Create(command.TenantId, command.Enabled, command.Note, command.ByUserId, DateTime.UtcNow)
            : Update(settings, command);
        if (changed.IsFailure)
            return Result.Failure<FormModuleSettingsResponse>(changed.Error);
        var capabilities = ApplyCapabilities(changed.Value, command);
        if (capabilities.IsFailure)
            return Result.Failure<FormModuleSettingsResponse>(capabilities.Error);
        if (settings is null)
            repository.Add(changed.Value);
        audit.Record(command.TenantId, null, null, FormAuditAction.ModuleAvailabilityChanged, command.ByUserId);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(FormModuleSettingsResponse.From(changed.Value));
    }

    private static Result ApplyCapabilities(
        FormModuleSettings settings,
        SetFormModuleAvailabilityCommand command
    )
    {
        if (command.ExternalParticipantsEnabled is null
            && command.IntakeLinksEnabled is null
            && command.AutoCustomerCreationEnabled is null
            && command.SignatureStepEnabled is null
            && command.PushNotificationsEnabled is null)
            return Result.Success();

        return settings.SetCapabilities(
            command.ExternalParticipantsEnabled ?? settings.ExternalParticipantsEnabled,
            command.IntakeLinksEnabled ?? settings.IntakeLinksEnabled,
            command.AutoCustomerCreationEnabled ?? settings.AutoCustomerCreationEnabled,
            command.SignatureStepEnabled ?? settings.SignatureStepEnabled,
            command.PushNotificationsEnabled ?? settings.PushNotificationsEnabled,
            command.ByUserId
        );
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
