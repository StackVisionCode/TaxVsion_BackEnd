namespace TaxVision.PaymentClient.Domain.Connect;

/// <summary>Progreso del flujo de onboarding hosted de Stripe — separado de
/// <see cref="ConnectAccountStatus"/> porque un tenant puede haber completado el formulario
/// (<see cref="Completed"/>) y aun así seguir <c>Restricted</c> si Stripe pide más adelante
/// documentación adicional (KYC continuo).</summary>
public enum OnboardingStep
{
    NotStarted = 1,
    LinkGenerated = 2,
    Completed = 3,
}
