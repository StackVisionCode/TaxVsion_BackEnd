using TaxVision.Customer.Domain.Imports;

namespace TaxVision.Customer.Application.Imports.Dtos;

public sealed record CustomerImportRowResponse(
    int RowNumber,
    RowStatus Status,
    Guid? ResultingCustomerId,
    string? DisplayName,
    string? MatchedBy,
    string? ErrorCode,
    string? Message
);
