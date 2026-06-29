using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports.Dtos;

namespace TaxVision.Customer.Application.Imports.Queries.GetCustomerImportAttempt;

public static class GetCustomerImportAttemptHandler
{
    public static async Task<Result<CustomerImportAttemptResponse>> Handle(
        GetCustomerImportAttemptQuery query,
        ICustomerImportReadService reader,
        CancellationToken ct
    )
    {
        var attempt = await reader.GetByIdAsync(query.ImportAttemptId, ct);
        if (attempt is null || attempt.TenantId != query.TenantId)
            return Result.Failure<CustomerImportAttemptResponse>(
                new Error("Import.NotFound", "Import attempt not found.")
            );

        return Result.Success(attempt);
    }
}
