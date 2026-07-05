namespace TaxVision.Notification.Domain.Emailing;

/// <summary>Ámbito de un recurso de email: global del SaaS (System) o propio del tenant.</summary>
public enum EmailScope
{
    System,
    Tenant,
}
