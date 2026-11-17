namespace TaxVision.Auth.Onboarding.Domain.TenantOnboardings;

/// <summary>
/// El descuento recurrente de un código del alta ("Months" o "Forever"): Auth lo guarda con la reserva y se lo
/// pasa a Subscription al activar la suscripción, que lo deja vigente desde el primer cobro.
/// </summary>
public sealed record OnboardingRecurringDiscount(
    Guid CodeDefinitionId,
    int? PercentBasisPoints,
    long? AmountCents,
    string? Currency,
    string Duration,
    int? Months
);
