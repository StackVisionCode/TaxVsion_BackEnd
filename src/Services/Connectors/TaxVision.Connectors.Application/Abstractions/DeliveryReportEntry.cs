namespace TaxVision.Connectors.Application.Abstractions;

/// <summary>
/// Un destinatario dentro de un aviso de no entrega (RFC 3464). Es protocolo, no negocio: qué significa
/// para el correo lo decide quien lo envió (Postmaster).
/// </summary>
/// <param name="OriginalMessageId">Message-ID del correo que no se pudo entregar.</param>
/// <param name="Action">failed, delayed, delivered, relayed o expanded.</param>
/// <param name="StatusCode">Código RFC 3463, ej. "5.1.1".</param>
public sealed record DeliveryReportEntry(
    string? OriginalMessageId,
    string? Recipient,
    string Action,
    string? StatusCode,
    string? Diagnostic
);
