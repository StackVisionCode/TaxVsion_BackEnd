namespace TaxVision.Signature.Application.Requests.Commands.Cancel;

/// <summary>
/// <paramref name="CanceledByUserDisplayName"/> es un snapshot del nombre que el
/// controller extrae del claim del access token, viaja por el evento integration
/// hasta la fila de la campana del CRM del dueño. Snapshot (no FK) para sobrevivir
/// a renombres/borrados del usuario que canceló.
/// </summary>
public sealed record CancelSignatureRequestCommand(
    Guid TenantId,
    Guid SignatureRequestId,
    Guid CanceledByUserId,
    string? CanceledByUserDisplayName,
    string? Reason
);
