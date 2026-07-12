namespace TaxVision.Customer.Domain.Audit;

/// <summary>Nombres de acción estables para <see cref="CustomerAuditLog"/> — mismo criterio que AuthAuditAction en Auth.</summary>
public static class CustomerAuditAction
{
    public const string FiscalProfileTaxIdentifierRevealed = "customer.fiscalprofile.tax_identifier_revealed";
}
