namespace TaxVision.Auth.Application.Terms;

/// <summary>Fase L1.4 — version vigente del ToS/AUP. Cambiarla obliga a todos los tenants a re-aceptar (ver TermsAcceptanceMiddleware).</summary>
public sealed class TermsOptions
{
    public const string SectionName = "Terms";

    public string CurrentVersion { get; set; } = "2026-07-14";
}
