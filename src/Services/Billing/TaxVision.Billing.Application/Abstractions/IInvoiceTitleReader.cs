namespace TaxVision.Billing.Application.Abstractions;

/// <summary>
/// Títulos en vivo del historial: de las facturas pedidas, solo las del tenant no borradas y, con
/// <paramref name="assignedToUserId"/>, las de clientes asignados a ese usuario (mismo filtro que la lectura).
/// </summary>
public interface IInvoiceTitleReader
{
    /// <param name="assignedToUserId">null = sin restricción (customers.view_all o flag apagado).</param>
    Task<IReadOnlyList<InvoiceTitleRow>> ListAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> invoiceIds,
        Guid? assignedToUserId,
        CancellationToken ct = default
    );
}

/// <summary>Id y número de una factura visible; el número es null mientras es borrador.</summary>
public sealed record InvoiceTitleRow(Guid Id, string? InvoiceNumber);
