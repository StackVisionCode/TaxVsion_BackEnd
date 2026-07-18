namespace TaxVision.Scribe.Domain;

/// <summary>Ámbito de un EmailTemplate/EmailLayout — System (global, TenantId null) o Tenant (override propio).</summary>
public enum TemplateScope
{
    System,
    Tenant,
}
