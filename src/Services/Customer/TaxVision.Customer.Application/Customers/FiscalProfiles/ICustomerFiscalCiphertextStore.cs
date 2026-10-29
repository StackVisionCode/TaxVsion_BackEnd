namespace TaxVision.Customer.Application.Customers.FiscalProfiles;

/// <summary>
/// Conteo y re-cifrado del ciphertext fiscal de Customer (B-9/X-9): SSN/EIN de perfiles de cliente y de
/// relaciones. El re-cifrado commitea cada lote, así que una interrupción no pierde lo hecho —las filas
/// re-cifradas quedan en la versión actual y salen del filtro en el siguiente arranque. El conteo es una
/// agregación por versión (GROUP BY sobre la columna), sin descifrar y sin tag de tenant (cardinalidad).
/// </summary>
public interface ICustomerFiscalCiphertextStore
{
    Task<IReadOnlyDictionary<short, long>> CountByKeyVersionAsync(CancellationToken ct = default);

    Task<CustomerFiscalReEncryptionReport> ReEncryptToCurrentAsync(int batchSize, CancellationToken ct = default);
}

public sealed record CustomerFiscalReEncryptionReport(
    short CurrentKeyVersion,
    IReadOnlyList<CustomerFiscalTableReEncryption> Tables
)
{
    public long Total => Tables.Sum(table => table.ReEncrypted);
}

public sealed record CustomerFiscalTableReEncryption(string Table, long ReEncrypted);
