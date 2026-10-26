using BuildingBlocks.Results;

namespace TaxVision.Tasks.ClientForms.Domain.Settings;

public static class FormModuleSettingsErrors
{
    public static readonly Error InvalidOwner = new(
        "FormModuleSettings.InvalidOwner",
        "Tenant and platform operator are required."
    );

    public static readonly Error InvalidSetting = new(
        "FormModuleSettings.InvalidSetting",
        "The module setting is invalid."
    );

    // La disponibilidad la decide el plan: no se puede apagar desde aquí.
    public static readonly Error EnabledNotSupported = new(
        "FormModuleSettings.EnabledNotSupported",
        "Client Forms availability follows the tenant plan (planner module) and cannot be turned off here."
    );

    public static readonly Error IntakeLinksNotReady = new(
        "FormModuleSettings.IntakeLinksNotReady",
        "Intake links cannot be enabled until bot protection is ready."
    );
}
