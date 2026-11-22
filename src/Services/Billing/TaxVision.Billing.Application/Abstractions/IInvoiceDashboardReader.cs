namespace TaxVision.Billing.Application.Abstractions;

/// <summary>
/// Totales del dashboard sumados en SQL: ninguna consulta trae filas, salvo las facturas más vencidas.
/// Todas respetan el borrado lógico, la moneda y el recorte por asignación de <see cref="InvoiceDashboardScope"/>.
/// </summary>
public interface IInvoiceDashboardReader
{
    /// <summary>Moneda de la factura más reciente visible para el actor; null si no hay facturas.</summary>
    Task<string?> LatestCurrencyAsync(Guid tenantId, Guid? assignedToUserId, CancellationToken ct = default);

    /// <summary>Cobrado en cada tramo [bounds[i], bounds[i+1]) por <c>PaidAtUtc</c>: pagos vigentes, sin crédito
    /// arrastrado, de facturas no borradas (las anuladas cuentan). Devuelve <c>bounds.Count - 1</c> sumas.</summary>
    Task<IReadOnlyList<long>> CollectedByRangeAsync(
        InvoiceDashboardScope scope,
        IReadOnlyList<DateTime> boundsUtc,
        CancellationToken ct = default
    );

    /// <summary>Facturado en cada tramo por <c>IssueDateUtc</c>, sin borradores ni anuladas.</summary>
    Task<IReadOnlyList<long>> InvoicedByRangeAsync(
        InvoiceDashboardScope scope,
        IReadOnlyList<DateTime> boundsUtc,
        CancellationToken ct = default
    );

    /// <summary>Pendiente de cobro (Issued, Sent o PartiallyPaid con saldo) por tramo de días vencida.</summary>
    Task<InvoiceAgingTotals> AgingAsync(
        InvoiceDashboardScope scope,
        InvoiceAgingCutoffs cutoffs,
        CancellationToken ct = default
    );

    /// <summary>Las facturas pendientes con la fecha base más antigua.</summary>
    Task<IReadOnlyList<OpenInvoiceRow>> OldestOpenAsync(
        InvoiceDashboardScope scope,
        int take,
        CancellationToken ct = default
    );
}

/// <param name="AssignedToUserId">null = toda la oficina; si no, solo los clientes asignados a ese usuario.</param>
public sealed record InvoiceDashboardScope(Guid TenantId, Guid? AssignedToUserId, string Currency);

/// <summary>Cortes UTC del aging. Una factura lleva k días vencida o más si su fecha base es anterior al corte de k
/// (inicio, en la zona de la oficina, del día siguiente a "hoy menos k").</summary>
public sealed record InvoiceAgingCutoffs(
    DateTime PastDueBeforeUtc,
    DateTime Over30BeforeUtc,
    DateTime Over60BeforeUtc,
    DateTime Over90BeforeUtc
);

public sealed record InvoiceAgingTotals(
    long OutstandingCents,
    int OpenCount,
    int PastDueCount,
    long UpTo30Cents,
    int UpTo30Count,
    long Days31To60Cents,
    int Days31To60Count,
    long Days61To90Cents,
    int Days61To90Count,
    long Over90Cents,
    int Over90Count
)
{
    public static InvoiceAgingTotals Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>Factura pendiente. <paramref name="BaseDateUtc"/> es el vencimiento, o la emisión si no lo tiene.</summary>
public sealed record OpenInvoiceRow(
    Guid InvoiceId,
    string? InvoiceNumber,
    Guid CustomerId,
    long DueCents,
    DateTime BaseDateUtc
);
