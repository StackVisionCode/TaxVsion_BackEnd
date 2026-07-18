namespace TaxVision.Connectors.Application.OAuth;

/// <summary>
/// Fallback de admin-consent (D3 §12.6), rescatado del legacy — solo Graph lo necesita (Google no
/// tiene un concepto equivalente), por eso vive en su propia interfaz en vez de ensuciar
/// <see cref="IOAuthProviderClient"/> con un método que Gmail nunca implementaría con sentido.
/// </summary>
public interface IMicrosoftAdminConsentClient
{
    /// <summary>
    /// URL a la que el frontend redirige cuando el connect normal (D3 §12.4) falló con
    /// AADSTS90094/consent_required — un admin del tenant de Microsoft la visita para otorgar
    /// consentimiento a nivel organización.
    /// </summary>
    string BuildAdminConsentUrl(string state);
}
