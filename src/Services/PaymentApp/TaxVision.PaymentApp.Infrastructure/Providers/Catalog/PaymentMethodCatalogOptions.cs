namespace TaxVision.PaymentApp.Infrastructure.Providers.Catalog;

public sealed class PaymentMethodCatalogOptions
{
    public const string SectionName = "PaymentMethods";

    public List<ConfiguredOnboardingPaymentMethod> Onboarding { get; set; } = [];
}
