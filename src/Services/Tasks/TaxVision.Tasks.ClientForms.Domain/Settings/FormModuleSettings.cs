using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Tasks.ClientForms.Domain.Settings;

public sealed class FormModuleSettings : TenantEntity
{
    public const int NoteMaxLength = 500;

    private FormModuleSettings() { }

    public bool Enabled { get; private set; }
    public DateTime? EnabledAtUtc { get; private set; }
    public Guid? EnabledByUserId { get; private set; }
    public string? Note { get; private set; }
    public bool ExternalParticipantsEnabled { get; private set; }
    public bool IntakeLinksEnabled { get; private set; }
    public bool AutoCustomerCreationEnabled { get; private set; }
    public bool SignatureStepEnabled { get; private set; }
    public bool PushNotificationsEnabled { get; private set; }

    public static Result<FormModuleSettings> Create(
        Guid tenantId,
        bool enabled,
        string? note,
        Guid byUserId,
        DateTime nowUtc
    )
    {
        if (tenantId == Guid.Empty || byUserId == Guid.Empty)
            return Result.Failure<FormModuleSettings>(FormModuleSettingsErrors.InvalidOwner);

        var settings = new FormModuleSettings();
        settings.SetTenant(tenantId);
        var updated = settings.SetAvailability(enabled, note, byUserId, nowUtc);
        return updated.IsFailure ? Result.Failure<FormModuleSettings>(updated.Error) : Result.Success(settings);
    }

    public Result SetAvailability(bool enabled, string? note, Guid byUserId, DateTime nowUtc)
    {
        var normalizedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (byUserId == Guid.Empty || normalizedNote?.Length > NoteMaxLength)
            return Result.Failure(FormModuleSettingsErrors.InvalidSetting);

        Enabled = enabled;
        EnabledAtUtc = enabled ? nowUtc : null;
        EnabledByUserId = byUserId;
        Note = normalizedNote;
        return Result.Success();
    }

    public Result SetCapabilities(
        bool externalParticipantsEnabled,
        bool intakeLinksEnabled,
        bool autoCustomerCreationEnabled,
        bool signatureStepEnabled,
        bool pushNotificationsEnabled,
        Guid byUserId
    )
    {
        if (byUserId == Guid.Empty)
            return Result.Failure(FormModuleSettingsErrors.InvalidSetting);

        ExternalParticipantsEnabled = externalParticipantsEnabled;
        IntakeLinksEnabled = intakeLinksEnabled;
        AutoCustomerCreationEnabled = autoCustomerCreationEnabled;
        SignatureStepEnabled = signatureStepEnabled;
        PushNotificationsEnabled = pushNotificationsEnabled;
        EnabledByUserId = byUserId;
        return Result.Success();
    }
}
