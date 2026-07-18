namespace TaxVision.Correspondence.Domain.Inbox;

/// <summary>
/// Ciclo de vida de la descarga bajo demanda de un attachment. Un <see cref="IncomingEmailAttachment"/>
/// nuevo siempre arranca en <see cref="NotRequested"/> — el flujo que transiciona el resto de
/// los estados (pedir el binario a Connectors, subirlo a CloudStorage) es Fase 12, todavía no existe.
/// </summary>
public enum AttachmentDownloadStatus
{
    NotRequested = 0,
    InProgress = 1,
    Downloaded = 2,
    Failed = 3,
}
