using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Tasks.ClientForms.Domain.Settings;

/// <summary>
/// Sub-flags de Client Forms por oficina. El módulo en sí depende solo del plan (planner): esta
/// fila ya no lo enciende ni lo apaga. Una oficina sin fila usa <see cref="FormModuleCapabilities.Defaults"/>.
/// </summary>
public sealed class FormModuleSettings : TenantEntity
{
    public const int NoteMaxLength = 500;

    private FormModuleSettings() { }

    // Columna heredada del interruptor por oficina: queda en la tabla, nadie la escribe ni la lee.
    [Obsolete("Client Forms depende solo del módulo planner; la columna Enabled quedó sin uso.")]
    public bool Enabled { get; private set; }

    // Histórico del interruptor retirado; ya no se escribe.
    public DateTime? EnabledAtUtc { get; private set; }
    public Guid? EnabledByUserId { get; private set; }
    public string? Note { get; private set; }
    public bool ExternalParticipantsEnabled { get; private set; }
    public bool IntakeLinksEnabled { get; private set; }
    public bool AutoCustomerCreationEnabled { get; private set; }
    public bool SignatureStepEnabled { get; private set; }
    public bool PushNotificationsEnabled { get; private set; }

    public FormModuleCapabilities Capabilities =>
        new(
            ExternalParticipantsEnabled,
            IntakeLinksEnabled,
            AutoCustomerCreationEnabled,
            SignatureStepEnabled,
            PushNotificationsEnabled
        );

    // Una fila nueva arranca con los mismos defaults que una oficina sin fila.
    public static Result<FormModuleSettings> Create(Guid tenantId, string? note, Guid byUserId)
    {
        if (tenantId == Guid.Empty || byUserId == Guid.Empty)
            return Result.Failure<FormModuleSettings>(FormModuleSettingsErrors.InvalidOwner);

        var settings = new FormModuleSettings();
        settings.SetTenant(tenantId);
        var defaults = FormModuleCapabilities.Defaults;
        settings.ExternalParticipantsEnabled = defaults.ExternalParticipantsEnabled;
        settings.IntakeLinksEnabled = defaults.IntakeLinksEnabled;
        settings.AutoCustomerCreationEnabled = defaults.AutoCustomerCreationEnabled;
        settings.SignatureStepEnabled = defaults.SignatureStepEnabled;
        settings.PushNotificationsEnabled = defaults.PushNotificationsEnabled;
        var updated = settings.SetNote(note, byUserId);
        return updated.IsFailure ? Result.Failure<FormModuleSettings>(updated.Error) : Result.Success(settings);
    }

    // Nota vacía o en blanco la borra; quien no quiera tocarla no llama a este método.
    public Result SetNote(string? note, Guid byUserId)
    {
        var normalizedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (byUserId == Guid.Empty || normalizedNote?.Length > NoteMaxLength)
            return Result.Failure(FormModuleSettingsErrors.InvalidSetting);

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

/// <summary>Sub-flags efectivos de una oficina, con o sin fila de settings.</summary>
public sealed record FormModuleCapabilities(
    bool ExternalParticipantsEnabled,
    bool IntakeLinksEnabled,
    bool AutoCustomerCreationEnabled,
    bool SignatureStepEnabled,
    bool PushNotificationsEnabled
)
{
    // Intake: O-16, sin Turnstile no se enciende. Firma: la real es P-18. Push encendido (decisión del dueño 2026-10-26).
    public static readonly FormModuleCapabilities Defaults = new(
        ExternalParticipantsEnabled: true,
        IntakeLinksEnabled: false,
        AutoCustomerCreationEnabled: true,
        SignatureStepEnabled: false,
        PushNotificationsEnabled: true
    );

    public static FormModuleCapabilities Of(FormModuleSettings? settings) => settings?.Capabilities ?? Defaults;
}
