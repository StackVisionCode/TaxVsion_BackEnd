namespace TaxVision.Postmaster.Domain.Providers;

/// <summary>
/// Ámbito de resolución de provider requerido por un envío. <c>System</c> siempre usa
/// <see cref="SystemEmailProvider"/>; <c>Tenant</c> exige un <see cref="TenantEmailProvider"/> propio
/// y NUNCA cae a System (política anti-spoofing, plan §14.5). <c>TenantOAuth</c> (D3) exige una
/// cuenta Gmail/Graph conectada vía Connectors — tampoco cae a <c>Tenant</c>-SMTP ni a
/// <c>System</c> si no hay cuenta conectada, mismo principio anti-spoofing (D3 §4.1).
/// </summary>
public enum ProviderScope
{
    System,
    Tenant,
    TenantOAuth,
}
