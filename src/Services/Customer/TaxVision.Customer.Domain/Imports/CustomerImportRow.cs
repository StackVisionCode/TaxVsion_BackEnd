using BuildingBlocks.Domain;

namespace TaxVision.Customer.Domain.Imports;

/// <summary>
/// Auditoria por fila del archivo importado. Permite generar el reporte descargable y
/// auditar exactamente que paso con cada registro sin tener que guardar el archivo original.
///
/// Esta entidad NO se muta desde fuera del aggregate root <see cref="CustomerImportAttempt"/>.
/// Toda mutacion va via los metodos RecordSuccess/RecordFailed/RecordSkipped/RecordUpdated
/// del aggregate, que internamente delegan aqui.
/// </summary>
public sealed class CustomerImportRow : TenantEntity
{
    private CustomerImportRow() { }

    public Guid CustomerImportAttemptId { get; private set; }
    public int RowNumber { get; private set; }
    public RowStatus Status { get; private set; }

    /// <summary>Id resultante: el customer creado o el matcheado al deduplicar.</summary>
    public Guid? ResultingCustomerId { get; private set; }

    /// <summary>Nombre de display para que el reporte sea legible sin descifrar PII.</summary>
    public string? DisplayName { get; private set; }

    /// <summary>Senial/criterio que disparo el match (SSN, Email, Phone, Name+DOB).</summary>
    public string? MatchedBy { get; private set; }

    /// <summary>Codigo de error de dominio para fallos (Customer.PersonalName, Address.City, etc).</summary>
    public string? ErrorCode { get; private set; }

    /// <summary>Mensaje humano. NUNCA debe contener SSN/EIN/datos bancarios.</summary>
    public string? Message { get; private set; }

    internal static CustomerImportRow CreatePending(Guid tenantId, Guid attemptId, int rowNumber)
    {
        var row = new CustomerImportRow
        {
            Id = Guid.Empty,
            CustomerImportAttemptId = attemptId,
            RowNumber = rowNumber,
            Status = RowStatus.Pending,
        };
        row.SetTenant(tenantId);
        return row;
    }

    internal void MarkSuccess(Guid customerId, string displayName)
    {
        Status = RowStatus.Success;
        ResultingCustomerId = customerId;
        DisplayName = Truncate(displayName, 200);
    }

    internal void MarkUpdated(Guid customerId, string displayName, string matchedBy)
    {
        Status = RowStatus.Updated;
        ResultingCustomerId = customerId;
        DisplayName = Truncate(displayName, 200);
        MatchedBy = Truncate(matchedBy, 200);
    }

    internal void MarkSkipped(Guid existingCustomerId, string displayName, string matchedBy)
    {
        Status = RowStatus.Skipped;
        ResultingCustomerId = existingCustomerId;
        DisplayName = Truncate(displayName, 200);
        MatchedBy = Truncate(matchedBy, 200);
        Message = "Skipped: duplicate detected, strategy=Skip.";
    }

    internal void MarkFailed(string? displayName, string errorCode, string message)
    {
        Status = RowStatus.Failed;
        DisplayName = Truncate(displayName, 200);
        ErrorCode = Truncate(errorCode, 80);
        Message = Truncate(message, 500);
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Length <= max ? value : value[..max];
    }
}
