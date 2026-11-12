using TaxVision.Subscription.Domain.Plans;

namespace TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;

public sealed class SubscriptionOptions
{
    public const string SectionName = "Subscriptions";

    // Respaldo del bootstrap; el valor real sale de la configuración (D1: enterprise).
    public string DefaultPlanCode { get; set; } = PlanCatalog.Enterprise;
    public int TrialDays { get; set; } = 14;
}
