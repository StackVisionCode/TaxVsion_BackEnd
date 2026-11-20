using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;

namespace TaxVision.Billing.Application.Invoices.GetInvoiceDetail;

/// <summary>Lectura RICA de una factura (incluye cliente y líneas) para prellenar el formulario de
/// edición. El listado/summary no trae líneas; esto sí.</summary>
public sealed record GetInvoiceDetailQuery(Guid TenantId, Guid InvoiceId, Guid ActorUserId, bool CanViewAll);

public sealed record InvoiceDetailCustomer(Guid CustomerId, string Name, string? Email, string? Phone, string? TaxId);

public sealed record InvoiceDetailLine(
    string Description,
    int Quantity,
    long UnitAmountCents,
    int TaxBasisPoints,
    Guid? CatalogItemId
);

/// <summary>Una fila del rastro de auditoría de estado (item 6.2).</summary>
public sealed record InvoiceStatusHistoryEntry(
    string? FromStatus,
    string ToStatus,
    string Trigger,
    string? Reason,
    Guid ChangedByUserId,
    DateTime ChangedAtUtc
);

/// <summary>Un pago aplicado a la factura. ExceedsDueCents &gt; 0 marca un sobrepago.</summary>
public sealed record InvoicePaymentEntry(
    string Source,
    long AmountCents,
    string Method,
    DateTime PaidAtUtc,
    Guid RecordedByUserId,
    long ExceedsDueCents,
    // Pago revertido por una corrección de estado: sigue en el historial pero ya no cuenta para lo pagado.
    DateTime? ReversedAtUtc = null,
    string? ReversalReason = null,
    // Era un cobro online: revertirlo no lo devuelve al cliente, hay que reembolsarlo en el proveedor.
    bool NeedsProviderRefund = false,
    // Crédito arrastrado que se revirtió: ya se cobró en la factura reemplazada y quedó sin aplicar.
    bool CreditLeftUnapplied = false
);

public sealed record InvoiceDetailResponse(
    Guid Id,
    string? InvoiceNumber,
    string Status,
    string Currency,
    string? Notes,
    InvoiceDetailCustomer Customer,
    IReadOnlyList<InvoiceDetailLine> Lines,
    long SubtotalCents,
    long TaxTotalCents,
    long TotalCents,
    long AmountPaidCents,
    bool IsEditable,
    bool IsVoidable,
    bool IsDeletable,
    /// <summary>Destinos legales de un cambio de estado MANUAL desde el estado actual (matriz del dominio).
    /// El frontend ofrece solo estos en el menú "Cambiar estado".</summary>
    IReadOnlyList<string> AllowedNextStatuses,
    /// <summary>Historial de transiciones, más reciente primero.</summary>
    IReadOnlyList<InvoiceStatusHistoryEntry> StatusHistory,
    /// <summary>Reemisión (item 6.3): se puede reemitir (anular + reemplazo enlazado) si está emitida/pagada
    /// y no fue reemplazada ya.</summary>
    bool IsReissuable,
    /// <summary>Si esta factura es un reemplazo, la original que sustituye; si fue reemplazada, su reemplazo.</summary>
    Guid? ReplacesInvoiceId,
    Guid? ReplacedByInvoiceId,
    /// <summary>Condiciones congeladas al crear la factura (null si es anterior a los ajustes de facturación).</summary>
    string? PaymentTerms = null,
    string? PaymentInstructions = null,
    string? InvoiceFooter = null,
    /// <summary>Pagos aplicados, más reciente primero.</summary>
    IReadOnlyList<InvoicePaymentEntry>? Payments = null,
    /// <summary>Lo pagado por encima del total (0 si no hay sobrepago).</summary>
    long OverpaidCents = 0,
    /// <summary>Estados a los que la oficina puede CORREGIR esta factura a mano (cualquiera menos el actual; ninguno si
    /// nunca se emitió). El frontend los ofrece solo con el permiso <c>invoicing.status.override</c>.</summary>
    IReadOnlyList<string>? OverridableStatuses = null
);

public static class GetInvoiceDetailHandler
{
    public static async Task<Result<InvoiceDetailResponse>> Handle(
        GetInvoiceDetailQuery query,
        IInvoiceRepository invoices,
        IOptions<BillingVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        var invoice = await invoices.GetByIdAsync(query.TenantId, query.InvoiceId, ct, assignedTo);
        if (invoice is null)
            return Result.Failure<InvoiceDetailResponse>(
                new Error("Billing.Invoice.NotFound", "Invoice does not exist.")
            );

        // Libertad total: se edita cualquier factura salvo una anulada (estado terminal).
        var editable = invoice.Status != Domain.ValueObjects.InvoiceStatus.Voided;
        var voidable =
            invoice.Status
            is Domain.ValueObjects.InvoiceStatus.Issued
                or Domain.ValueObjects.InvoiceStatus.Sent
                or Domain.ValueObjects.InvoiceStatus.PartiallyPaid
                or Domain.ValueObjects.InvoiceStatus.Paid;
        var deletable = invoice.Status == Domain.ValueObjects.InvoiceStatus.Draft;
        // Reemisión (6.3): emitida/enviada/parcial/pagada y no reemplazada aún.
        var reissuable =
            invoice.ReplacedByInvoiceId is null
            && invoice.Status
                is Domain.ValueObjects.InvoiceStatus.Issued
                    or Domain.ValueObjects.InvoiceStatus.Sent
                    or Domain.ValueObjects.InvoiceStatus.PartiallyPaid
                    or Domain.ValueObjects.InvoiceStatus.Paid;

        var allowedNext = Domain
            .Invoices.InvoiceStatusTransitions.AllowedTargets(invoice.Status)
            .Select(s => s.ToString())
            .ToList();

        var history = invoice
            .StatusChanges.OrderByDescending(s => s.ChangedAtUtc)
            .Select(s => new InvoiceStatusHistoryEntry(
                s.FromStatus?.ToString(),
                s.ToStatus.ToString(),
                s.Trigger,
                s.Reason,
                s.ChangedByUserId,
                s.ChangedAtUtc
            ))
            .ToList();

        var payments = invoice
            .Payments.OrderByDescending(p => p.PaidAtUtc)
            .Select(p => new InvoicePaymentEntry(
                p.Source.ToString(),
                p.Amount.AmountCents,
                p.Method.ToString(),
                p.PaidAtUtc,
                p.RecordedByUserId,
                p.ExceedsDueCents,
                p.ReversedAtUtc,
                p.ReversalReason,
                p.ReversedAtUtc is not null && p.NeedsProviderRefund,
                p.ReversedAtUtc is not null && p.IsCarriedCredit
            ))
            .ToList();

        return Result.Success(
            new InvoiceDetailResponse(
                invoice.Id,
                invoice.InvoiceNumber,
                invoice.Status.ToString(),
                invoice.Currency,
                invoice.Notes,
                new InvoiceDetailCustomer(
                    invoice.Customer.CustomerId,
                    invoice.Customer.Name,
                    invoice.Customer.Email,
                    invoice.Customer.Phone,
                    invoice.Customer.TaxId
                ),
                invoice
                    .Lines.Select(l => new InvoiceDetailLine(
                        l.Description,
                        l.Quantity,
                        l.UnitAmount.AmountCents,
                        l.TaxBasisPoints,
                        l.CatalogItemId
                    ))
                    .ToList(),
                invoice.Subtotal.AmountCents,
                invoice.TaxTotal.AmountCents,
                invoice.Total.AmountCents,
                invoice.AmountPaid.AmountCents,
                editable,
                voidable,
                deletable,
                allowedNext,
                history,
                reissuable,
                invoice.ReplacesInvoiceId,
                invoice.ReplacedByInvoiceId,
                invoice.PaymentTerms?.ToString(),
                invoice.PaymentInstructions,
                invoice.InvoiceFooter,
                payments,
                Math.Max(0, invoice.AmountPaid.AmountCents - invoice.Total.AmountCents),
                OverridableStatusesFor(invoice)
            )
        );
    }

    /// <summary>Destinos de una corrección de estado: cualquiera de los elegibles menos el actual. Una factura que nunca
    /// se emitió no tiene número y se emite, no se corrige.</summary>
    internal static IReadOnlyList<string> OverridableStatusesFor(Domain.Invoices.Invoice invoice)
    {
        if (
            invoice.DeletedAtUtc is not null
            || invoice.InvoiceNumber is null
            || invoice.ReplacedByInvoiceId is not null
        )
            return [];

        return Enum.GetValues<Domain.ValueObjects.InvoiceStatus>()
            .Where(status => status != invoice.Status && Domain.Invoices.Invoice.IsOverridableTarget(status))
            .Select(status => status.ToString())
            .ToList();
    }
}
