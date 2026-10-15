using TaxVision.Subscription.Domain.Plans;

namespace TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;

public sealed class SubscriptionOptions
{
    public const string SectionName = "Subscriptions";

    public string DefaultPlanCode { get; set; } = PlanCatalog.Starter;
    public int TrialDays { get; set; } = 14;
}
