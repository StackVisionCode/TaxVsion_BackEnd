using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;

namespace TaxVision.Auth.Application.Onboarding.Failures;

public enum FailureKind
{
    Transient,
    Permanent,
}

/// <summary>PayFlow (Fase 17) — clasifica un fallo de paso de provisioning para decidir si el
/// retry scheduler puede reintentarlo automáticamente o si debe ir directo a ManualReview.
/// <para>
/// Regla especial: <see cref="TenantProvisioningStep.TenantAdmin"/> es SIEMPRE
/// <see cref="FailureKind.Permanent"/>, sin importar el <c>FailureCode</c>. Motivo: el paso consume
/// una referencia Redis de un solo uso (<c>PasswordHashReference</c>, GETDEL) que la propia Saga
/// pone a null en memoria apenas construye <c>CreateTenantOwnerCommand</c> (ver
/// <c>TenantOnboardingProcessManager.Handle(TenantCreatedForOnboardingIntegrationEvent)</c>) — para
/// cuando este paso puede fallar, la referencia original ya no está disponible ni en la Saga ni
/// (probablemente) en Redis. Reintentar el mismo comando con esa referencia nula o
/// consumida es imposible; requiere remediación manual (el admin no puede simplemente "reintentar").
/// </para>
/// <para>
/// Para el resto de los pasos, la regla es por sufijo del código: los códigos que terminan en
/// <c>.RequestFailed</c>/<c>.UnexpectedStatus</c>/<c>.EmptyResponse</c> los producen los HttpClients
/// M2M (<c>TenantProvisioningClient</c>, <c>SubscriptionActivationClient</c>, etc.) cuando la llamada
/// ni siquiera llegó a ejecutarse en el servicio destino (red, timeout, 5xx) — reintentar tiene
/// sentido. Cualquier otro código es un error de dominio devuelto POR el servicio destino (p.ej.
/// <c>Subscription.Onboarding.PlanNotFound</c>, <c>Tenant.Subdomain</c>) — reintentar sin cambiar el
/// dato de entrada nunca va a funcionar, así que se clasifica Permanent por defecto (ante duda, no
/// reintentar indefinidamente un bug o dato inválido; el admin decide vía
/// <c>update-and-resume</c>/<c>force-complete</c>/<c>cancel-and-refund</c>).
/// </para></summary>
public static class FailureClassifier
{
    private static readonly string[] TransientCodeSuffixes = [".RequestFailed", ".UnexpectedStatus", ".EmptyResponse"];

    public static FailureKind Classify(TenantProvisioningStep failedStep, string failureCode)
    {
        if (failedStep == TenantProvisioningStep.TenantAdmin)
            return FailureKind.Permanent;

        if (string.IsNullOrWhiteSpace(failureCode))
            return FailureKind.Permanent;

        foreach (var suffix in TransientCodeSuffixes)
        {
            if (failureCode.EndsWith(suffix, StringComparison.Ordinal))
                return FailureKind.Transient;
        }

        return FailureKind.Permanent;
    }
}
