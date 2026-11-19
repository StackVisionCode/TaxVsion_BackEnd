namespace TaxVision.Wallet.Infrastructure.Persistence;

/// <summary>
/// Rellena <c>LedgerEntries.ReferenceType</c> de los asientos viejos desde su <c>OperationKey</c> (D2). Solo escribe
/// la columna nueva y solo donde está vacía; no toca montos ni claves. Formatos:
/// <c>topup:{id}</c> → "topup"; <c>wallet-{reserve|consume|release|credit|clawback}:{tipo}:{id}</c> → {tipo}.
/// Va en <c>EXEC sp_executesql</c>: SQL Server compila el lote antes de que exista la columna recién agregada.
/// </summary>
public static class LedgerReferenceTypeBackfill
{
    public const string Sql = """
        EXEC sp_executesql N'
        UPDATE [LedgerEntries] SET [ReferenceType] = N''topup''
        WHERE [ReferenceType] IS NULL AND [OperationKey] LIKE N''topup:%'';

        UPDATE e SET [ReferenceType] = SUBSTRING(e.[OperationKey], p.[First] + 1, p.[Second] - p.[First] - 1)
        FROM [LedgerEntries] AS e
        CROSS APPLY (SELECT CHARINDEX(N'':'', e.[OperationKey]) AS [First]) AS f
        CROSS APPLY (SELECT f.[First], CHARINDEX(N'':'', e.[OperationKey], f.[First] + 1) AS [Second]) AS p
        WHERE e.[ReferenceType] IS NULL
          AND (e.[OperationKey] LIKE N''wallet-reserve:%''
            OR e.[OperationKey] LIKE N''wallet-consume:%''
            OR e.[OperationKey] LIKE N''wallet-release:%''
            OR e.[OperationKey] LIKE N''wallet-credit:%''
            OR e.[OperationKey] LIKE N''wallet-clawback:%'')
          AND p.[Second] > p.[First] + 1
          AND p.[Second] - p.[First] - 1 <= 64;'
        """;
}
