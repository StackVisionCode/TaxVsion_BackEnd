namespace TaxVision.Subscription.Application.AddOns.Commands.CreateModuleAddOn;

/// <summary>Crea y publica un add-on de módulo (habilita un module.*) con precio mensual y anual. PlatformAdmin.</summary>
public sealed record CreateModuleAddOnCommand(
    string Code,
    string Name,
    string Module,
    decimal MonthlyUsd,
    decimal YearlyUsd,
    Guid ActorUserId
);
