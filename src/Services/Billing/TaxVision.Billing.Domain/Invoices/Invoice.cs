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
    private readonly List<InvoiceAdjustmentLine> _adjustments = [];

    public string? InvoiceNumber { get; private set; }
    public InvoiceStatus Status { get; private set; }

    /// <summary>Onboarding pago-primero: la factura nace PRE-TENANT (dueño = PlatformTenant.Id) y se
    /// re-hospeda al tenant real cuando la saga lo activa (<see cref="RehomeToTenant"/>). Null en
    /// facturas normales tenant→taxpayer.</summary>
    public Guid? OnboardingId { get; private set; }

    /// <summary>Plan de suscripción facturado (solo onboarding). Null en facturas normales.</summary>
    public Guid? PlanId { get; private set; }

    /// <summary>Cómo se liquidó (solo onboarding): Paid / Mixed / FullyCoveredByCode. Null en el resto.</summary>
    public SettlementType? SettlementType { get; private set; }

    /// <summary>Id del pago real que liquidó la factura (SaaSPayment), si hubo cobro. NULL cuando el
    /// código cubrió el 100% (regla: solo net &gt; 0 genera Payment).</summary>
    public Guid? PaymentId { get; private set; }
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
    public IReadOnlyCollection<InvoiceAdjustmentLine> Adjustments => _adjustments;

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

    /// <summary>
    /// Fábrica de la factura de ONBOARDING pago-primero. Billing es la fuente de verdad financiera:
    /// se crea una factura para TODA operación comercial, incluso cuando el total es $0 y no hubo pago.
    /// Nace ya liquidada (Issued+Paid en un solo tiro) porque el pago/redención ya ocurrió aguas arriba.
    /// El dueño inicial es <paramref name="tenantId"/> (PlatformTenant.Id, pre-tenant) y se re-hospeda al
    /// tenant real con <see cref="RehomeToTenant"/>. Montos: bruto (Subtotal) − descuentos (DiscountTotal)
    /// = neto (Total). Reglas: descuento ≤ bruto; neto = bruto − descuento; la suma de ajustes = descuento;
    /// solo <c>net &gt; 0</c> lleva <paramref name="paymentId"/> (net = 0 ⇒ FullyCoveredByCode, sin pago).
    /// </summary>
    public static Result<Invoice> CreateForOnboarding(
        Guid tenantId,
        Guid onboardingId,
        Guid planId,
        Guid? paymentId,
        string invoiceNumber,
        CustomerSnapshot customer,
        IssuerSnapshot issuer,
        string planDescription,
        long grossAmountCents,
        long discountAmountCents,
        long netAmountCents,
        string currency,
        SettlementType settlementType,
        IReadOnlyList<OnboardingInvoiceAdjustment> adjustments,
        DateTime nowUtc,
        DateTime dueDateUtc
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<Invoice>(new Error("Billing.Invoice.InvalidOwner", "Owner tenant is required."));
        if (onboardingId == Guid.Empty)
            return Result.Failure<Invoice>(new Error("Billing.Invoice.InvalidOnboarding", "OnboardingId is required."));
        if (customer is null)
            return Result.Failure<Invoice>(new Error("Billing.Invoice.CustomerRequired", "Customer is required."));
        if (issuer is null)
            return Result.Failure<Invoice>(new Error("Billing.Invoice.IssuerRequired", "Issuer is required."));
        if (string.IsNullOrWhiteSpace(invoiceNumber))
            return Result.Failure<Invoice>(new Error("Billing.Invoice.NumberRequired", "Invoice number is required."));
        if (string.IsNullOrWhiteSpace(planDescription))
            return Result.Failure<Invoice>(
                new Error("Billing.Invoice.LineDescription", "Line description is required.")
            );

        var currencyCheck = Money.Create(0, currency);
        if (currencyCheck.IsFailure)
            return Result.Failure<Invoice>(currencyCheck.Error);
        var cur = currencyCheck.Value.Currency;

        if (grossAmountCents < 0 || discountAmountCents < 0 || netAmountCents < 0)
            return Result.Failure<Invoice>(new Error("Billing.Invoice.NegativeAmount", "Amounts cannot be negative."));
        if (discountAmountCents > grossAmountCents)
            return Result.Failure<Invoice>(
                new Error("Billing.Invoice.DiscountExceedsGross", "Discount cannot exceed gross amount.")
            );
        if (grossAmountCents - discountAmountCents != netAmountCents)
            return Result.Failure<Invoice>(
                new Error("Billing.Invoice.InvalidNet", "Net amount must equal gross minus discount.")
            );

        // Solo una operación con neto > 0 genera un Payment real; net = 0 ⇒ cubierta por código, sin pago.
        if (netAmountCents > 0 && paymentId is null)
            return Result.Failure<Invoice>(
                new Error("Billing.Invoice.PaymentRequired", "A positive net amount requires a PaymentId.")
            );
        if (netAmountCents == 0 && paymentId is not null)
            return Result.Failure<Invoice>(
                new Error("Billing.Invoice.UnexpectedPayment", "A fully-covered invoice must not carry a PaymentId.")
            );

        var adjustmentTotal = adjustments?.Sum(a => a.AmountCents) ?? 0;
        if (adjustmentTotal != discountAmountCents)
            return Result.Failure<Invoice>(
                new Error("Billing.Invoice.AdjustmentMismatch", "The sum of adjustments must equal the discount total.")
            );

        var invoice = new Invoice
        {
            Status = InvoiceStatus.Paid,
            Currency = cur,
            Customer = customer,
            Issuer = issuer,
            OnboardingId = onboardingId,
            PlanId = planId,
            PaymentId = paymentId,
            SettlementType = settlementType,
            InvoiceNumber = invoiceNumber,
            IssueDateUtc = nowUtc,
            DueDateUtc = dueDateUtc,
            PaidAtUtc = nowUtc,
            PaymentMethod = paymentId is null ? ValueObjects.PaymentMethod.Other : ValueObjects.PaymentMethod.Online,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            CreatedBy = Guid.Empty,
        };
        invoice.SetTenant(tenantId);

        // Línea de cargo del plan (bruto) + una línea de ajuste por cada beneficio aplicado.
        invoice._lines.Add(
            new InvoiceLineItem(
                invoice.Id,
                planDescription.Trim(),
                1,
                Money.Create(grossAmountCents, cur).Value,
                0,
                Money.Zero(cur),
                Money.Create(grossAmountCents, cur).Value
            )
        );
        foreach (var adj in adjustments ?? [])
        {
            invoice._adjustments.Add(
                new InvoiceAdjustmentLine(
                    invoice.Id,
                    adj.Type,
                    adj.Code,
                    adj.GrowthReservationId,
                    Money.Create(adj.AmountCents, cur).Value,
                    nowUtc
                )
            );
        }

        invoice.Subtotal = Money.Create(grossAmountCents, cur).Value;
        invoice.TaxTotal = Money.Zero(cur);
        invoice.DiscountTotal = Money.Create(discountAmountCents, cur).Value;
        invoice.Total = Money.Create(netAmountCents, cur).Value;
        invoice.AmountPaid = Money.Create(netAmountCents, cur).Value;
        invoice.AmountDue = Money.Zero(cur);
        invoice.ReceiptNumber = $"REC-{invoiceNumber}";
        invoice.ReceiptHash = invoice.ComputeReceiptHash(netAmountCents, invoice.PaymentMethod.Value, nowUtc);

        return Result.Success(invoice);
    }

    /// <summary>Re-hospeda una factura de onboarding (creada bajo PlatformTenant.Id) al tenant real, una
    /// vez que la saga lo crea/activa. Idempotente: reasignar el mismo tenant es no-op. Solo aplica a
    /// facturas de onboarding.</summary>
    public Result RehomeToTenant(Guid realTenantId, DateTime nowUtc)
    {
        if (realTenantId == Guid.Empty)
            return Result.Failure(new Error("Billing.Invoice.InvalidOwner", "Real tenant is required."));
        if (OnboardingId is null)
            return Result.Failure(
                new Error("Billing.Invoice.NotOnboarding", "Only an onboarding invoice can be re-homed to a tenant.")
            );
        if (TenantId == realTenantId)
            return Result.Success();

        SetTenant(realTenantId);
        UpdatedAtUtc = nowUtc;
        return Result.Success();
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

/// <summary>Ajuste (descuento) de entrada para la factura de onboarding. <see cref="AmountCents"/> es la
/// magnitud positiva del descuento; la suma de todos debe igualar el descuento total.</summary>
public sealed record OnboardingInvoiceAdjustment(
    InvoiceAdjustmentType Type,
    string? Code,
    Guid? GrowthReservationId,
    long AmountCents
);
