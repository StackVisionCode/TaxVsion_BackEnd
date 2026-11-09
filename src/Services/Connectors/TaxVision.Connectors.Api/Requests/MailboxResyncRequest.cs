namespace TaxVision.Connectors.Api.Requests;

/// <summary>Ventana [FromUtc, ToUtc) a re-sincronizar. El tenant tiene que ser el del token de servicio.</summary>
public sealed record MailboxResyncRequest(Guid TenantId, DateTime FromUtc, DateTime ToUtc);
