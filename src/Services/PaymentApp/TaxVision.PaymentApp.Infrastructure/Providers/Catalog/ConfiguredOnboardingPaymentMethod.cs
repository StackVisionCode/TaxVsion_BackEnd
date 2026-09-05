namespace TaxVision.PaymentApp.Infrastructure.Providers.Catalog;

public sealed class ConfiguredOnboardingPaymentMethod
{
    public string Provider { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 100;
    public string? DisabledReason { get; set; }
    public List<string> PlanIds { get; set; } = [];
    public List<string> BillingCycles { get; set; } = [];
    public List<string> Currencies { get; set; } = [];
}
