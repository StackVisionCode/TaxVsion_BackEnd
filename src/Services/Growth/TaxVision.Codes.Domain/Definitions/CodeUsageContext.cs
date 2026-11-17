namespace TaxVision.Codes.Domain.Definitions;

/// <summary>Dónde se puede usar un código: al registrarse, en una suscripción existente o en ambos.</summary>
public enum CodeUsageContext
{
    Signup = 1,
    ExistingSubscription = 2,
    Both = 3,
}
