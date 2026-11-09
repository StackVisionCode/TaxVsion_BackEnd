using TaxVision.Billing.Domain.Invoices;

namespace TaxVision.Billing.Application.Abstractions;

/// <summary>
/// Prefiltro en SQL del job de reconciliación (BIL-3b), entre todos los tenants: facturas no borradas con
/// <c>UpdatedAtUtc</c> en [<paramref name="notBeforeUtc"/>, <paramref name="updatedBeforeUtc"/>) y marca de
/// sincronización vacía o vieja. La decisión final la toma <c>InvoiceSyncPolicy</c>. Sin seguimiento.
/// </summary>
public interface IInvoiceSyncCandidateQuery
{
    Task<IReadOnlyList<Invoice>> ListAsync(
        DateTime notBeforeUtc,
        DateTime updatedBeforeUtc,
        int take,
        CancellationToken ct = default
    );
}
