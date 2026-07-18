using TaxVision.Customer.Application.Imports.Dtos;

namespace TaxVision.Customer.Application.Abstractions;

public interface ICustomerImportReadService
{
    Task<CustomerImportAttemptResponse?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<CustomerImportAttemptResponse>> SearchAsync(
        Guid tenantId,
        int page,
        int size,
        CancellationToken ct
    );

    /// <summary>Streamea las filas del reporte sin cargar todo en memoria.</summary>
    IAsyncEnumerable<CustomerImportRowResponse> StreamRowsAsync(Guid importId, CancellationToken ct);
}
