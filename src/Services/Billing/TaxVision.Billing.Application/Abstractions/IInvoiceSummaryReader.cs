using TaxVision.Billing.Domain.ValueObjects;

namespace TaxVision.Billing.Application.Abstractions;

/// <summary>
/// Lo mínimo de CADA factura de la oficina para calcular sus totales (cobrado, por cobrar, tiempo medio de cobro,
/// borradores). No es la tabla: la tabla trae las últimas N; los totales cuentan todas.
/// </summary>
public interface IInvoiceSummaryReader
{
    /// <param name="assignedToUserId">null = sin restricción (customers.view_all o flag apagado); si no, solo las de
    /// clientes asignados a ese usuario (mismo filtro que la lectura).</param>
    Task<IReadOnlyList<InvoiceSummaryRow>> ListAsync(
        Guid tenantId,
        Guid? assignedToUserId,
        CancellationToken ct = default
    );
}

/// <summary>Fila mínima para los totales. Los importes van en centavos de <paramref name="Currency"/>.</summary>
public sealed record InvoiceSummaryRow(
    InvoiceStatus Status,
    string Currency,
    long AmountDueCents,
    long AmountPaidCents,
    DateTime CreatedAtUtc,
    DateTime? PaidAtUtc
);
