namespace TaxVision.Subscription.Application.Billing.Commands.SetBillingPreference;

/// <summary>El monto/currency/cadencia los resuelve el handler contra el plan vigente del tenant — nunca
/// se aceptan del request para evitar que el frontend autorice un cobro distinto al pactado.</summary>
public sealed record SetBillingPreferenceCommand(
    Guid TenantId,
    Guid ActorUserId,
    string Mode,
    string? ProviderCode,
    string? ConsentText,
    string? ConsentTextVersion,
    string? ConsentIpAddress,
    string? ConsentUserAgent
);
