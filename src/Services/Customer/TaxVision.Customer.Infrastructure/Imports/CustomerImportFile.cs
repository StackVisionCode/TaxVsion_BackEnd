namespace TaxVision.Customer.Infrastructure.Imports;

/// <summary>
/// Storage temporal del binario del archivo de import. Vive solo entre POST y la finalizacion del worker.
/// Implementacion-detail de Infrastructure: NO se expone a Domain ni Application.
///
/// Cuando exista CloudStorage Service, este store se reemplaza por un fileId externo y esta tabla
/// se elimina.
/// </summary>
internal sealed class CustomerImportFile
{
    public Guid ImportAttemptId { get; set; }
    public byte[] Content { get; set; } = default!;
    public DateTime UploadedAtUtc { get; set; }
}
