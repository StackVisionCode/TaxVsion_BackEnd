namespace TaxVision.Scribe.Domain;

/// <summary>
/// Quién lee el correo. Decide qué logo de la oficina va en el header: el equipo ve el del CRM y el
/// cliente el del portal (con el del CRM de respaldo). Staff es el default para que todo caller que no
/// lo indica siga igual que antes.
/// </summary>
public enum RenderAudience
{
    Staff,
    Client,
}
