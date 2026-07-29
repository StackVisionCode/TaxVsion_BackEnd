using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Billing.Domain.ValueObjects;

namespace TaxVision.Billing.Domain.Invoices;

/// <summary>
/// Aggregate root del documento factura tenant→taxpayer. Dueño único de su ciclo de vida,
/// totales congelados, líneas y enlaces de pago. No conoce providers de pago, render de PDF,
/// envío de email ni el bus — solo junta domain events; drenarlos es del DbContext.
///
/// SCAFFOLD B1: la máquina de estados y las transiciones completas (Issue/MarkSent/RecordPayment/
/// Void/…) se implementan en la fase B2 (ver documents/architecture/billing/15_Billing_Implementation_Plan.md).
/// </summary>
public sealed class Invoice : AggregateRoot
{
    private readonly List<InvoiceLineItem> _lines = [];
    private readonly List<InvoicePaymentLink> _paymentLinks = [];

    public string? InvoiceNumber { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public DateTime IssueDateUtc { get; private set; }
    public DateTime DueDateUtc { get; private set; }
    public DateTime? SentAtUtc { get; private set; }
    public DateTime? PaidAtUtc { get; private set; }

    public CustomerSnapshot Customer { get; private set; } = null!;
    public IssuerSnapshot? Issuer { get; private set; }
    public Discount? Discount { get; private set; }

    public Money Subtotal { get; private set; } = null!;
    public Money TaxTotal { get; private set; } = null!;
    public Money DiscountTotal { get; private set; } = null!;
    public Money Total { get; private set; } = null!;
    public Money AmountPaid { get; private set; } = null!;
    public Money AmountDue { get; private set; } = null!;
    public string Currency { get; private set; } = "USD";

    public string? PoNumber { get; private set; }
    public string? Summary { get; private set; }
    public string? Notes { get; private set; }
    public PaymentMethod? PaymentMethod { get; private set; }

    public Guid? PdfFileId { get; private set; }
    public Guid? PaidPdfFileId { get; private set; }

    /// <summary>Comprobante de pago: número de recibo + hash de verificación (SHA-256), generados al
    /// marcar la factura pagada. Null hasta que se paga. El hash es reproducible desde
    /// (tenant, factura, monto, moneda, método, fecha) → permite verificar el recibo.</summary>
    public string? ReceiptNumber { get; private set; }
    public string? ReceiptHash { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid? LastModifiedBy { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<InvoiceLineItem> Lines => _lines;
    public IReadOnlyCollection<InvoicePaymentLink> PaymentLinks => _paymentLinks;

    private Invoice() { }

    /// <summary>
    /// Fábrica del borrador (B2, Fase 1). Valida líneas/moneda, congela el snapshot del cliente y
    /// calcula los totales provisionales. Billing snapshotea montos ya calculados por el caller
    /// (no recalcula reglas fiscales; solo el impuesto por línea desde los basis points provistos).
    /// </summary>
    public static Result<Invoice> CreateDraft(
        Guid tenantId,
        Guid actorUserId,
        CustomerSnapshot customer,
        string currency,
        IReadOnlyList<DraftInvoiceLine> lines,
        string? notes,
        DateTime nowUtc,
        IssuerSnapshot? issuer = null
    )
    {
        if (customer is null)
            return Result.Failure<Invoice>(new Error("Billing.Invoice.CustomerRequired", "Customer is required."));
        if (lines is null || lines.Count == 0)
            return Result.Failure<Invoice>(new Error("Billing.Invoice.NoLines", "An invoice needs at least one line."));

        var currencyCheck = Money.Create(0, currency);
        if (currencyCheck.IsFailure)
            return Result.Failure<Invoice>(currencyCheck.Error);
        var cur = currencyCheck.Value.Currency;

        var invoice = new Invoice
        {
            Status = InvoiceStatus.Draft,
            Currency = cur,
            Customer = customer,
            Issuer = issuer,
            Notes = notes,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            CreatedBy = actorUserId,
        };
        invoice.SetTenant(tenantId);

        long subtotalCents = 0;
        long taxCents = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line.Description))
                return Result.Failure<Invoice>(
                    new Error("Billing.Invoice.LineDescription", "Line description is required.")
                );
            if (line.Quantity <= 0)
                return Result.Failure<Invoice>(
                    new Error("Billing.Invoice.LineQuantity", "Line quantity must be greater than zero.")
                );
            if (line.UnitAmountCents < 0)
                return Result.Failure<Invoice>(
                    new Error("Billing.Invoice.LineAmount", "Line unit amount cannot be negative.")
                );
            if (line.TaxBasisPoints is < 0 or > 100_000)
                return Result.Failure<Invoice>(
                    new Error("Billing.Invoice.LineTax", "Tax basis points must be between 0 and 100000.")
                );

            var lineSubtotal = line.UnitAmountCents * line.Quantity;
            var lineTax = (long)
                Math.Round(lineSubtotal * (line.TaxBasisPoints / 10_000.0), MidpointRounding.AwayFromZero);
            var lineTotal = lineSubtotal + lineTax;

            invoice._lines.Add(
                new InvoiceLineItem(
                    invoice.Id,
                    line.Description.Trim(),
                    line.Quantity,
                    Money.Create(line.UnitAmountCents, cur).Value,
                    line.TaxBasisPoints,
                    Money.Create(lineTax, cur).Value,
                    Money.Create(lineTotal, cur).Value
                )
            );

            subtotalCents += lineSubtotal;
            taxCents += lineTax;
        }

        invoice.Subtotal = Money.Create(subtotalCents, cur).Value;
        invoice.TaxTotal = Money.Create(taxCents, cur).Value;
        invoice.DiscountTotal = Money.Zero(cur);
        invoice.Total = Money.Create(subtotalCents + taxCents, cur).Value;
        invoice.AmountPaid = Money.Zero(cur);
        invoice.AmountDue = invoice.Total;

        return Result.Success(invoice);
    }

    /// <summary>Emite el borrador: le asigna el número server-side y las fechas, y lo mueve a Issued.
    /// A partir de acá la factura es inmutable en montos.</summary>
    public Result Issue(string invoiceNumber, DateTime nowUtc, DateTime dueDateUtc, Guid actorUserId)
    {
        if (Status != InvoiceStatus.Draft)
            return Result.Failure(
                new Error("Billing.Invoice.NotDraft", $"Only a Draft invoice can be issued (current: {Status}).")
            );
        if (string.IsNullOrWhiteSpace(invoiceNumber))
            return Result.Failure(new Error("Billing.Invoice.NumberRequired", "Invoice number is required."));

        InvoiceNumber = invoiceNumber;
        IssueDateUtc = nowUtc;
        DueDateUtc = dueDateUtc;
        Status = InvoiceStatus.Issued;
        UpdatedAtUtc = nowUtc;
        LastModifiedBy = actorUserId;
        return Result.Success();
    }

    /// <summary>Correlaciona el PDF generado por Documents (FileId de CloudStorage) con la factura.
    /// Idempotente: reasignar el mismo FileId es no-op efectivo.</summary>
    public void AttachPdf(Guid fileId, DateTime nowUtc)
    {
        PdfFileId = fileId;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>El enlace de cobro Active más reciente, si hay alguno. Se usa para reusar la URL estable
    /// en reintentos de generación del PDF en vez de asegurar un payable nuevo cada vez.</summary>
    public InvoicePaymentLink? ActivePaymentLink =>
        _paymentLinks
            .Where(l => l.Status == InvoicePaymentLinkStatus.Active)
            .OrderByDescending(l => l.CreatedAtUtc)
            .FirstOrDefault();

    /// <summary>Correlaciona la factura con su ancla estable de cobro en PaymentClient (PayableReference)
    /// y guarda la URL estable para el botón/QR del PDF. Idempotente por ExternalPayableId: reintentar
    /// con el mismo payable no duplica.</summary>
    public InvoicePaymentLink AttachPaymentLink(Guid externalPayableId, string checkoutUrl, DateTime nowUtc)
    {
        var existing = _paymentLinks.FirstOrDefault(l => l.ExternalPayableId == externalPayableId);
        if (existing is not null)
            return existing;

        // NO se muta la factura (UpdatedAtUtc/RowVersion): adjuntar un enlace hijo solo inserta esa fila.
        // Tocar el padre dispararía un UPDATE con chequeo de RowVersion que compite con el resto del
        // pipeline post-emisión (concurrencia optimista → 0 filas). El enlace es correlación, no muta la factura.
        var link = new InvoicePaymentLink(Id, externalPayableId, checkoutUrl, nowUtc);
        _paymentLinks.Add(link);
        return link;
    }

    /// <summary>Registra un cobro exitoso (Fase 3). Idempotente: si ya está <see cref="InvoiceStatus.Paid"/>
    /// es no-op — así reprocesar el mismo evento (o uno posterior) no duplica el pago. Valida que la moneda
    /// coincida. Marca <see cref="InvoiceStatus.Paid"/> si cubre el total, o <see cref="InvoiceStatus.PartiallyPaid"/>
    /// si no. El caller (consumer) valida además el monto contra el total antes de llamar.</summary>
    public Result MarkPaid(long amountCents, string currency, DateTime paidAtUtc, ValueObjects.PaymentMethod method)
    {
        if (Status == InvoiceStatus.Paid)
            return Result.Success();

        if (Status is not (InvoiceStatus.Issued or InvoiceStatus.Sent or InvoiceStatus.PartiallyPaid))
            return Result.Failure(
                new Error("Billing.Invoice.NotPayable", $"Cannot pay an invoice in status {Status}.")
            );

        if (!string.Equals(currency, Currency, StringComparison.OrdinalIgnoreCase))
            return Result.Failure(
                new Error(
                    "Billing.Invoice.CurrencyMismatch",
                    $"Payment currency {currency} does not match invoice currency {Currency}."
                )
            );

        AmountPaid = Money.Create(amountCents, Currency).Value;
        var dueCents = Total.AmountCents - amountCents;
        AmountDue = Money.Create(dueCents < 0 ? 0 : dueCents, Currency).Value;
        Status = amountCents >= Total.AmountCents ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid;
        if (Status == InvoiceStatus.Paid)
        {
            PaidAtUtc = paidAtUtc;
            ReceiptNumber = $"REC-{InvoiceNumber}";
            ReceiptHash = ComputeReceiptHash(amountCents, method, paidAtUtc);
        }
        PaymentMethod = method;
        UpdatedAtUtc = paidAtUtc;
        return Result.Success();
    }

    /// <summary>Hash de verificación del recibo (SHA-256 hex). Reproducible: cualquiera con los mismos
    /// datos del pago obtiene el mismo hash → sirve para verificar que el recibo no fue alterado.</summary>
    private string ComputeReceiptHash(long amountCents, ValueObjects.PaymentMethod method, DateTime paidAtUtc)
    {
        var canonical = $"{TenantId:N}|{Id:N}|{InvoiceNumber}|{amountCents}|{Currency}|{method}|{paidAtUtc:O}";
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes);
    }
}

/// <summary>Línea de entrada para crear un borrador. El impuesto se deriva de los basis points;
/// Billing no recalcula reglas fiscales, solo el prorrateo aritmético.</summary>
public sealed record DraftInvoiceLine(string Description, int Quantity, long UnitAmountCents, int TaxBasisPoints);
