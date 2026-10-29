namespace TaxVision.Customer.Domain.Audit;

/// <summary>Nombres de acción estables para <see cref="CustomerAuditLog"/> — mismo criterio que AuthAuditAction en Auth.</summary>
public static class CustomerAuditAction
{
    public const string FiscalProfileTaxIdentifierRevealed = "customer.fiscalprofile.tax_identifier_revealed";

    // P-13 — perfil actualizado desde un formulario de cliente (sin valores en Details).
    public const string ProfileUpdatedFromForm = "customer.profile.updated_from_form";
    public const string FiscalProfileUpdatedFromForm = "customer.fiscalprofile.updated_from_form";
}
