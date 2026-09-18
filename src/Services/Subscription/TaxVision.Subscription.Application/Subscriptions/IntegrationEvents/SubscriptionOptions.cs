using TaxVision.Subscription.Domain.Plans;

namespace TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;

public sealed class SubscriptionOptions
{
    public const string SectionName = "Subscriptions";

    public string DefaultPlanCode { get; set; } = PlanCatalog.Starter;
    public int TrialDays { get; set; } = 14;

    /// <summary>Días de gracia tras agotarse los reintentos de renovación (PastDue→GracePeriod) antes de
    /// suspender. Ventana final para que el tenant actualice el pago sin perder acceso.</summary>
    public int GracePeriodDays { get; set; } = 7;
}
