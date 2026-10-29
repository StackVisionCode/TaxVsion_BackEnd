using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Audit;
using TaxVision.Tasks.ClientForms.Domain.Settings;
using Wolverine;

namespace TaxVision.Tasks.ClientForms.Application.Settings;

// Sub-flags y nota; lo omitido (null) se conserva y Note "" la borra. El módulo lo decide el plan:
// Enabled true u omitido se ignora, false se rechaza para no aparentar que apagó algo.
public sealed record SetFormModuleAvailabilityCommand(
    Guid TenantId,
    Guid ByUserId,
    string? Note,
    bool? ExternalParticipantsEnabled = null,
    bool? IntakeLinksEnabled = null,
    bool? AutoCustomerCreationEnabled = null,
    bool? SignatureStepEnabled = null,
    bool? PushNotificationsEnabled = null,
    bool? Enabled = null
);

public static class SetFormModuleAvailabilityHandler
{
    public static async Task<Result<FormModuleSettingsResponse>> Handle(
        SetFormModuleAvailabilityCommand command,
        IFormModuleSettingsRepository repository,
        IFormAuditLog audit,
        IFormIntakeBotProtection botProtection,
        IClientFormsAvailabilityEvaluator availability,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        if (command.Enabled == false)
            return Result.Failure<FormModuleSettingsResponse>(FormModuleSettingsErrors.EnabledNotSupported);
        if (command.IntakeLinksEnabled == true && !botProtection.IsReady)
            return Result.Failure<FormModuleSettingsResponse>(FormModuleSettingsErrors.IntakeLinksNotReady);
        var settings = await repository.GetAsync(command.TenantId, ct);
        var changed = settings is null
            ? FormModuleSettings.Create(command.TenantId, command.Note, command.ByUserId)
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
        if (command.PushNotificationsEnabled.HasValue)
            await bus.PublishAsync(NotificationSettings(command, changed.Value, correlation.CorrelationId));
        var module = await availability.EvaluateAsync(command.TenantId, ct);
        return Result.Success(FormModuleSettingsResponse.From(command.TenantId, module, changed.Value));
    }

    private static Result ApplyCapabilities(FormModuleSettings settings, SetFormModuleAvailabilityCommand command)
    {
        if (
            command.ExternalParticipantsEnabled is null
            && command.IntakeLinksEnabled is null
            && command.AutoCustomerCreationEnabled is null
            && command.SignatureStepEnabled is null
            && command.PushNotificationsEnabled is null
        )
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
        if (command.Note is null)
            return Result.Success(settings);
        var result = settings.SetNote(command.Note, command.ByUserId);
        return result.IsFailure ? Result.Failure<FormModuleSettings>(result.Error) : Result.Success(settings);
    }

    private static ClientFormNotificationSettingsSyncRequestedIntegrationEvent NotificationSettings(
        SetFormModuleAvailabilityCommand command,
        FormModuleSettings settings,
        string correlationId
    )
    {
        var commandId = Guid.NewGuid();
        return new()
        {
            EventId = commandId,
            CommandId = commandId,
            TenantId = command.TenantId,
            CorrelationId = correlationId,
            PushNotificationsEnabled = settings.PushNotificationsEnabled,
        };
    }
}
