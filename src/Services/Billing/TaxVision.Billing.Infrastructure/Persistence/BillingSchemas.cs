namespace TaxVision.Billing.Infrastructure.Persistence;

/// <summary>Fronteras físicas dentro de la única base TaxVision_Billing. Sin FKs cross-schema
/// ni cross-service.</summary>
public static class BillingSchemas
{
    public const string Billing = "billing";
    public const string Integration = "integration";
    public const string Audit = "audit";
}
