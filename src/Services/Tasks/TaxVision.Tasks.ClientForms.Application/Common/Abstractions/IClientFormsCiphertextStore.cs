namespace TaxVision.Tasks.ClientForms.Application.Common.Abstractions;

/// <summary>
/// Acceso a los conteos y al re-cifrado del ciphertext de Client Forms (X-9). El re-cifrado commitea
/// cada lote, así que una interrupción no pierde lo ya hecho: las filas re-cifradas quedan en la versión
/// actual y salen del filtro en el siguiente arranque. El conteo es una agregación por versión, sin descifrar.
/// </summary>
public interface IClientFormsCiphertextStore
{
    // Filas por versión de clave, sumando todas las tablas cifradas. Sin tag de tenant (cardinalidad).
    Task<IReadOnlyDictionary<short, long>> CountByKeyVersionAsync(CancellationToken ct = default);

    // Re-cifra por lotes toda fila cuya versión no sea la actual. Idempotente y reanudable.
    Task<ClientFormsReEncryptionReport> ReEncryptToCurrentAsync(int batchSize, CancellationToken ct = default);
}

public sealed record ClientFormsReEncryptionReport(
    short CurrentKeyVersion,
    IReadOnlyList<ClientFormsTableReEncryption> Tables
)
{
    public long Total => Tables.Sum(table => table.ReEncrypted);
}

public sealed record ClientFormsTableReEncryption(string Table, long ReEncrypted);
