namespace TaxVision.Customer.Application.Imports.Dtos;

/// <summary>
/// Resultado del detector de duplicados para una fila del chunk.
/// </summary>
public sealed record DuplicateMatch(
    int RowNumber,
    Guid ExistingCustomerId,
    string ExistingDisplayName,
    string MatchedBy
);
