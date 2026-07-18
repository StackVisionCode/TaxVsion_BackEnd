namespace TaxVision.Correspondence.Domain.Inbox;

/// <summary>
/// Por qué un <c>connectors.raw_message_received.v1</c> terminó en <see cref="UnmatchedIncomingEmail"/>
/// en vez de convertirse en un <see cref="IncomingEmail"/> real. Ver plan de diseño §36 Fase 4.
/// </summary>
public enum UnmatchedReason
{
    /// <summary>El remitente (<c>From</c>) no matcheó ningún <c>CustomerEmailAddress</c> activo del tenant.</summary>
    NoCustomerMatch = 0,

    /// <summary>
    /// El remitente sí matcheó un customer conocido, pero las señales de autenticación
    /// (SPF/DKIM/DMARC) indican que el mensaje probablemente está spoofeando esa dirección
    /// (plan §36 Fase 4, punto b.1) — se trata como cuarentena de seguridad, no como ruido de debug.
    /// </summary>
    AuthenticationFailed = 1,
}
