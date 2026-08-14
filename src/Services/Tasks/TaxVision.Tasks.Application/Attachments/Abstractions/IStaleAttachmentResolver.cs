namespace TaxVision.Tasks.Application.Attachments.Abstractions;

/// <summary>
/// Cierra los adjuntos que se quedaron esperando un veredicto que ya se emitió. Devuelve cuántos
/// resolvió: cero sostenido es la lectura sana.
/// </summary>
public interface IStaleAttachmentResolver
{
    Task<int> ResolveAsync(TimeSpan olderThan, int batchSize, CancellationToken ct = default);
}
