namespace TaxVision.Customer.Application.Abstractions;

/// <summary>
/// Storage temporal para el binario del archivo entre el endpoint POST y el handler del worker.
/// Implementacion sencilla en Infrastructure: guarda el byte[] en una tabla efimera o disco temporal.
///
/// TODO: cuando exista CloudStorage Service, reemplazar por un fileId. Por ahora el archivo es
/// pequeño (max 10k filas ~= ~5MB CSV) y vive en una tabla CustomerImportFiles que se purga al completar.
/// </summary>
public interface IImportFileStore
{
    /// <summary>Guarda el archivo asociado al attempt y devuelve cuando este durable en BD.</summary>
    Task SaveAsync(Guid importAttemptId, byte[] content, CancellationToken ct);

    /// <summary>Lee el archivo como stream (el worker lo procesa en chunks).</summary>
    Task<Stream> OpenReadAsync(Guid importAttemptId, CancellationToken ct);

    /// <summary>Borra el binario despues de procesar para liberar espacio (el reporte por fila queda en CustomerImportRows).</summary>
    Task DeleteAsync(Guid importAttemptId, CancellationToken ct);
}
