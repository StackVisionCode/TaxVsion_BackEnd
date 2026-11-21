using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.Invoices.Events;
using TaxVision.Billing.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Billing.Tests.Domain;

/// <summary>
/// Corrección de estado a gusto de la oficina: cualquier estado → Draft/Issued/Sent/Paid/Voided con motivo. Volver a
/// "sin cobrar" revierte los pagos (siguen en el historial); marcar como pagada registra el pago del saldo.
/// </summary>
public sealed class InvoiceStatusOverrideTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorUserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
    private const string Why = "Corrección pedida por la oficina";

    [Fact]
    public void A_paid_invoice_can_go_back_to_draft_reversing_its_payments_and_keeping_its_number()
    {
        var invoice = PaidInvoice(10_000, InvoicePaymentSource.Manual);
        invoice.ClearDomainEvents();

        var result = invoice.OverrideStatus(InvoiceStatus.Draft, Why, Now.AddHours(1), ActorUserId);

        Assert.True(result.IsSuccess);
        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.Equal("INV-2026-00001", invoice.InvoiceNumber);
        Assert.Equal(0, invoice.AmountPaid.AmountCents);
        Assert.Equal(10_000, invoice.AmountDue.AmountCents);
        Assert.Null(invoice.ReceiptNumber);
        Assert.Null(invoice.PaidAtUtc);
        // El pago NO se borra: queda revertido, con quién y por qué.
        var payment = Assert.Single(invoice.Payments);
        Assert.False(payment.IsActive);
        Assert.Equal(ActorUserId, payment.ReversedByUserId);
        Assert.Equal(Why, payment.ReversalReason);
        Assert.Single(result.Value.ReversedPayments);
        Assert.Empty(result.Value.RefundsDueAtProvider);
    }

    [Fact]
    public void Reversing_an_online_payment_says_it_must_be_refunded_at_the_provider()
    {
        var invoice = PaidInvoice(10_000, InvoicePaymentSource.Online);

        var result = invoice.OverrideStatus(InvoiceStatus.Issued, Why, Now.AddHours(1), ActorUserId);

        Assert.True(result.IsSuccess);
        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
        var refund = Assert.Single(result.Value.RefundsDueAtProvider);
        Assert.Equal(InvoicePaymentSource.Online, refund.Source);
    }

    [Fact]
    public void A_partially_paid_invoice_reopened_to_sent_reverses_the_partial_payment()
    {
        var invoice = IssuedInvoice(10_000);
        Apply(invoice, InvoicePaymentSource.Manual, "a", 4_000);
        Assert.Equal(InvoiceStatus.PartiallyPaid, invoice.Status);

        var result = invoice.OverrideStatus(InvoiceStatus.Sent, Why, Now.AddHours(1), ActorUserId);

        Assert.True(result.IsSuccess);
        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
        Assert.NotNull(invoice.SentAtUtc);
        Assert.Equal(0, invoice.AmountPaid.AmountCents);
        Assert.Equal(10_000, invoice.AmountDue.AmountCents);
    }

    [Fact]
    public void A_reversed_payment_redelivered_by_the_provider_is_not_applied_twice()
    {
        var invoice = PaidInvoice(10_000, InvoicePaymentSource.Online, "stripe-1");
        Assert.True(invoice.OverrideStatus(InvoiceStatus.Issued, Why, Now, ActorUserId).IsSuccess);

        // Stripe reentrega el mismo cobro: sigue siendo el mismo (Source, Reference) → no-op.
        Assert.True(Apply(invoice, InvoicePaymentSource.Online, "stripe-1", 10_000).IsSuccess);

        Assert.Single(invoice.Payments);
        Assert.Equal(0, invoice.AmountPaid.AmountCents);
        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
    }

    [Fact]
    public void An_unpaid_invoice_can_be_marked_paid_recording_a_manual_payment_for_the_balance()
    {
        var invoice = IssuedInvoice(10_000);
        invoice.ClearDomainEvents();

        var result = invoice.OverrideStatus(InvoiceStatus.Paid, Why, Now.AddHours(1), ActorUserId);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.PaymentRecorded);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(10_000, invoice.AmountPaid.AmountCents);
        Assert.Equal(0, invoice.AmountDue.AmountCents);
        Assert.NotNull(invoice.ReceiptNumber);
        var payment = Assert.Single(invoice.Payments);
        Assert.Equal(InvoicePaymentSource.Manual, payment.Source);
        // Sale el aviso de "pagada" como siempre, y una sola fila de auditoría con el motivo.
        Assert.Single(invoice.DomainEvents.OfType<InvoicePaidDomainEvent>());
        var audit = invoice.StatusChanges.Last();
        Assert.Equal(StatusChangeTrigger.StatusOverride, audit.Trigger);
        Assert.Equal(Why, audit.Reason);
    }

    [Fact]
    public void A_reopened_draft_can_be_marked_paid_going_through_issued()
    {
        var invoice = PaidInvoice(10_000, InvoicePaymentSource.Manual);
        Assert.True(invoice.OverrideStatus(InvoiceStatus.Draft, Why, Now, ActorUserId).IsSuccess);

        var result = invoice.OverrideStatus(InvoiceStatus.Paid, Why, Now.AddHours(2), ActorUserId);

        Assert.True(result.IsSuccess);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(10_000, invoice.AmountPaid.AmountCents);
        Assert.Equal(2, invoice.Payments.Count); // el revertido (historial) + el nuevo
        Assert.Single(invoice.Payments.Where(p => p.IsActive));
    }

    [Fact]
    public void Voiding_keeps_the_payments_like_the_normal_void_does()
    {
        var invoice = PaidInvoice(10_000, InvoicePaymentSource.Manual);

        Assert.True(invoice.OverrideStatus(InvoiceStatus.Voided, Why, Now, ActorUserId).IsSuccess);

        Assert.Equal(InvoiceStatus.Voided, invoice.Status);
        Assert.Equal(Why, invoice.VoidReason);
        Assert.NotNull(invoice.VoidedAtUtc);
        Assert.True(invoice.Payments.Single().IsActive);
    }

    [Fact]
    public void Leaving_voided_clears_the_void_data()
    {
        var invoice = IssuedInvoice(10_000);
        Assert.True(invoice.OverrideStatus(InvoiceStatus.Voided, Why, Now, ActorUserId).IsSuccess);

        Assert.True(invoice.OverrideStatus(InvoiceStatus.Sent, Why, Now.AddHours(1), ActorUserId).IsSuccess);

        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
        Assert.Null(invoice.VoidedAtUtc);
        Assert.Null(invoice.VoidReason);
    }

    [Fact]
    public void Going_to_draft_withdraws_the_payment_links()
    {
        var invoice = IssuedInvoice(10_000);
        invoice.AttachPaymentLink(Guid.NewGuid(), "https://pay.example.com/x", Now);
        Assert.NotNull(invoice.ActivePaymentLink);

        Assert.True(invoice.OverrideStatus(InvoiceStatus.Draft, Why, Now, ActorUserId).IsSuccess);

        Assert.Null(invoice.ActivePaymentLink);
        Assert.Null(invoice.PaymentLinkSyncedDue);
    }

    // La reemisión vuelve a asegurar el MISMO payable: el enlace retirado tiene que quedar activo otra vez.
    [Fact]
    public void Reissuing_a_reopened_draft_brings_its_payment_link_back()
    {
        var invoice = IssuedInvoice(10_000);
        var payableId = Guid.NewGuid();
        invoice.AttachPaymentLink(payableId, "https://pay.example.com/x", Now);
        Assert.True(invoice.OverrideStatus(InvoiceStatus.Draft, Why, Now, ActorUserId).IsSuccess);
        var number = InvoiceNumber.Create(invoice.InvoiceNumber!).Value;
        Assert.True(invoice.Issue(number, Now.AddHours(1), Now.AddDays(30), ActorUserId).IsSuccess);

        var link = invoice.AttachPaymentLink(payableId, "https://pay.example.com/x", Now.AddHours(1));

        Assert.Same(link, invoice.ActivePaymentLink);
        Assert.Single(invoice.PaymentLinks);
    }

    // La original ya entregó su crédito al reemplazo: revivirla dejaría dos facturas cobradas con el mismo dinero.
    [Theory]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Issued)]
    [InlineData(InvoiceStatus.Sent)]
    [InlineData(InvoiceStatus.Draft)]
    public void A_replaced_invoice_cannot_be_brought_back(InvoiceStatus target)
    {
        var original = PaidInvoice(10_000, InvoicePaymentSource.Manual);
        var replacement = original.CreateReplacementDraft(ActorUserId, Now).Value;
        Assert.True(original.ReissueAs(replacement, Why, Now, ActorUserId).IsSuccess);

        var result = original.OverrideStatus(target, Why, Now.AddHours(1), ActorUserId);

        Assert.True(result.IsFailure);
        Assert.Equal("Billing.Invoice.Replaced", result.Error.Code);
        Assert.Equal(InvoiceStatus.Voided, original.Status);
        Assert.Single(original.Payments, p => p.IsActive);
    }

    // Tuvo número y cobros: se anula, no se borra (dejaría un hueco en la secuencia y perdería el historial).
    [Fact]
    public void A_reopened_draft_that_keeps_its_number_cannot_be_deleted()
    {
        var invoice = PaidInvoice(10_000, InvoicePaymentSource.Manual);
        Assert.True(invoice.OverrideStatus(InvoiceStatus.Draft, Why, Now, ActorUserId).IsSuccess);

        var result = invoice.SoftDeleteDraft(Now.AddHours(1), ActorUserId);

        Assert.True(result.IsFailure);
        Assert.Equal("Billing.Invoice.NumberedDraft", result.Error.Code);
        Assert.Null(invoice.DeletedAtUtc);
    }

    [Fact]
    public void A_draft_that_was_never_issued_can_still_be_deleted()
    {
        var draft = DraftInvoice(10_000);

        Assert.True(draft.SoftDeleteDraft(Now, ActorUserId).IsSuccess);
        Assert.NotNull(draft.DeletedAtUtc);
    }

    [Fact]
    public void Every_override_is_audited_and_announced()
    {
        var invoice = IssuedInvoice(10_000);
        invoice.ClearDomainEvents();

        Assert.True(invoice.OverrideStatus(InvoiceStatus.Sent, Why, Now.AddHours(1), ActorUserId).IsSuccess);

        var audit = invoice.StatusChanges.Last();
        Assert.Equal(InvoiceStatus.Issued, audit.FromStatus);
        Assert.Equal(InvoiceStatus.Sent, audit.ToStatus);
        Assert.Equal(StatusChangeTrigger.StatusOverride, audit.Trigger);
        Assert.Equal(ActorUserId, audit.ChangedByUserId);
        var changed = Assert.IsType<InvoiceStatusChangedDomainEvent>(Assert.Single(invoice.DomainEvents));
        Assert.Equal(InvoiceStatus.Sent, changed.ToStatus);
        Assert.Equal(StatusChangeTrigger.StatusOverride, changed.Trigger);
    }

    [Fact]
    public void The_same_status_is_a_no_op_without_audit_or_events()
    {
        var invoice = IssuedInvoice(10_000);
        invoice.ClearDomainEvents();
        var auditRows = invoice.StatusChanges.Count;

        var result = invoice.OverrideStatus(InvoiceStatus.Issued, Why, Now, ActorUserId);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Changed);
        Assert.Equal(auditRows, invoice.StatusChanges.Count);
        Assert.Empty(invoice.DomainEvents);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ab")]
    public void A_reason_is_required(string? reason)
    {
        var invoice = IssuedInvoice(10_000);

        var result = invoice.OverrideStatus(InvoiceStatus.Sent, reason, Now, ActorUserId);

        Assert.True(result.IsFailure);
        Assert.Equal("Billing.Invoice.ReasonRequired", result.Error.Code);
        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
    }

    [Fact]
    public void Partially_paid_is_not_a_status_you_can_pick()
    {
        var invoice = IssuedInvoice(10_000);

        var result = invoice.OverrideStatus(InvoiceStatus.PartiallyPaid, Why, Now, ActorUserId);

        Assert.True(result.IsFailure);
        Assert.Equal("Billing.Invoice.StatusNotSettable", result.Error.Code);
    }

    [Fact]
    public void A_draft_that_was_never_issued_must_be_issued_not_corrected()
    {
        var invoice = DraftInvoice(10_000);

        var result = invoice.OverrideStatus(InvoiceStatus.Paid, Why, Now, ActorUserId);

        Assert.True(result.IsFailure);
        Assert.Equal("Billing.Invoice.NeverIssued", result.Error.Code);
        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
    }

    // Las columnas del motivo son de 500/512: uno más largo terminaba en un error 500 al guardar.
    [Fact]
    public void A_reason_longer_than_500_characters_is_rejected()
    {
        var invoice = PaidInvoice(10_000, InvoicePaymentSource.Manual);

        var result = invoice.OverrideStatus(InvoiceStatus.Issued, new string('x', 501), Now, ActorUserId);

        Assert.True(result.IsFailure);
        Assert.Equal("Billing.Invoice.ReasonTooLong", result.Error.Code);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.True(invoice.Payments.Single().IsActive);
    }

    [Fact]
    public void A_reason_of_exactly_500_characters_is_accepted_ignoring_outer_spaces()
    {
        var invoice = PaidInvoice(10_000, InvoicePaymentSource.Manual);
        var reason = new string('x', 500);

        var result = invoice.OverrideStatus(InvoiceStatus.Issued, $"  {reason}  ", Now, ActorUserId);

        Assert.True(result.IsSuccess);
        Assert.Equal(reason, invoice.Payments.Single().ReversalReason);
    }

    // El crédito arrastrado ya se cobró en la factura reemplazada, que quedó anulada y no se puede revivir: si se
    // revierte, la oficina tiene que enterarse de que ese dinero quedó sin aplicar.
    [Theory]
    [InlineData(InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Issued)]
    [InlineData(InvoiceStatus.Sent)]
    public void Reversing_a_carried_credit_reports_it_as_money_left_unapplied(InvoiceStatus target)
    {
        var replacement = ReplacementPaidWithCarriedCredit(10_000);

        var result = replacement.OverrideStatus(target, Why, Now.AddHours(2), ActorUserId);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, replacement.AmountPaid.AmountCents);
        var credit = Assert.Single(result.Value.CreditsLeftUnapplied);
        Assert.Equal(InvoicePaymentSource.CarriedCredit, credit.Source);
        Assert.Equal(10_000, credit.Amount.AmountCents);
        Assert.False(credit.IsActive);
        // No es un cobro del proveedor de esta factura: el aviso es otro.
        Assert.Empty(result.Value.RefundsDueAtProvider);
    }

    [Fact]
    public void Reversing_ordinary_payments_leaves_no_credit_unapplied()
    {
        var invoice = PaidInvoice(10_000, InvoicePaymentSource.Online);

        var result = invoice.OverrideStatus(InvoiceStatus.Issued, Why, Now.AddHours(1), ActorUserId);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.CreditsLeftUnapplied);
    }

    // Factura anterior a la tabla de pagos: lo pagado no tiene fila. Al revertir tiene que quedar constancia del importe.
    [Fact]
    public void A_paid_invoice_without_payment_rows_leaves_a_reversed_payment_in_the_history()
    {
        var invoice = LegacyPaidInvoice(10_000, 10_000, PaymentMethod.Cash);

        var result = invoice.OverrideStatus(InvoiceStatus.Issued, Why, Now.AddHours(1), ActorUserId);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, invoice.AmountPaid.AmountCents);
        Assert.Equal(10_000, invoice.AmountDue.AmountCents);
        var payment = Assert.Single(invoice.Payments);
        Assert.Equal(InvoicePaymentSource.Legacy, payment.Source);
        Assert.Equal(10_000, payment.Amount.AmountCents);
        Assert.Equal(PaymentMethod.Cash, payment.Method);
        Assert.Equal(Now, payment.PaidAtUtc);
        Assert.False(payment.IsActive);
        Assert.Equal(ActorUserId, payment.ReversedByUserId);
        Assert.Equal(Why, payment.ReversalReason);
        Assert.Same(payment, Assert.Single(result.Value.ReversedPayments));
        Assert.Empty(result.Value.RefundsDueAtProvider);
    }

    [Fact]
    public void A_legacy_invoice_paid_online_says_it_must_be_refunded_at_the_provider()
    {
        var invoice = LegacyPaidInvoice(10_000, 10_000, PaymentMethod.Online);

        var result = invoice.OverrideStatus(InvoiceStatus.Draft, Why, Now.AddHours(1), ActorUserId);

        Assert.True(result.IsSuccess);
        var refund = Assert.Single(result.Value.RefundsDueAtProvider);
        Assert.Equal(InvoicePaymentSource.Legacy, refund.Source);
        Assert.Equal(10_000, refund.Amount.AmountCents);
    }

    [Fact]
    public void Only_the_part_without_payment_rows_gets_a_legacy_row()
    {
        var invoice = LegacyPaidInvoice(10_000, 4_000, PaymentMethod.Cash);
        Assert.True(Apply(invoice, InvoicePaymentSource.Manual, "rest", 6_000).IsSuccess);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);

        var result = invoice.OverrideStatus(InvoiceStatus.Sent, Why, Now.AddHours(1), ActorUserId);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, invoice.AmountPaid.AmountCents);
        Assert.Equal(2, result.Value.ReversedPayments.Count);
        Assert.Equal(6_000, invoice.Payments.Single(p => p.Source == InvoicePaymentSource.Manual).Amount.AmountCents);
        Assert.Equal(4_000, invoice.Payments.Single(p => p.Source == InvoicePaymentSource.Legacy).Amount.AmountCents);
        Assert.DoesNotContain(invoice.Payments, p => p.IsActive);
    }

    [Fact]
    public void Marking_a_legacy_paid_invoice_as_voided_adds_no_payment_row()
    {
        var invoice = LegacyPaidInvoice(10_000, 10_000, PaymentMethod.Cash);

        Assert.True(invoice.OverrideStatus(InvoiceStatus.Voided, Why, Now.AddHours(1), ActorUserId).IsSuccess);

        Assert.Empty(invoice.Payments);
        Assert.Equal(10_000, invoice.AmountPaid.AmountCents);
    }

    /// <summary>Reemplazo emitido y saldado con el crédito que arrastró de la original (pagada en línea).</summary>
    private static Invoice ReplacementPaidWithCarriedCredit(long totalCents)
    {
        var original = PaidInvoice(totalCents, InvoicePaymentSource.Online);
        var replacement = original.CreateReplacementDraft(ActorUserId, Now).Value;
        Assert.True(original.ReissueAs(replacement, Why, Now, ActorUserId).IsSuccess);
        Assert.True(
            replacement
                .Issue(InvoiceNumber.Create("INV-2026-00002").Value, Now.AddHours(1), Now.AddDays(30), ActorUserId)
                .IsSuccess
        );
        Assert.True(replacement.ApplyCarriedCredit(Now.AddHours(1)).IsSuccess);
        Assert.Equal(InvoiceStatus.Paid, replacement.Status);
        return replacement;
    }

    /// <summary>Factura cobrada antes de que existiera la tabla de pagos: tiene lo pagado pero ninguna fila.</summary>
    private static Invoice LegacyPaidInvoice(long totalCents, long paidCents, PaymentMethod method)
    {
        var invoice = IssuedInvoice(totalCents);
        Seed(invoice, nameof(Invoice.AmountPaid), Money.Create(paidCents, "USD").Value);
        Seed(invoice, nameof(Invoice.AmountDue), Money.Create(totalCents - paidCents, "USD").Value);
        Seed(invoice, nameof(Invoice.PaymentMethod), method);
        if (paidCents >= totalCents)
        {
            Seed(invoice, nameof(Invoice.Status), InvoiceStatus.Paid);
            Seed(invoice, nameof(Invoice.PaidAtUtc), Now);
        }
        else
        {
            Seed(invoice, nameof(Invoice.Status), InvoiceStatus.PartiallyPaid);
        }
        Assert.Empty(invoice.Payments);
        return invoice;
    }

    private static void Seed(Invoice invoice, string property, object? value) =>
        typeof(Invoice).GetProperty(property)!.SetValue(invoice, value);

    private static BuildingBlocks.Results.Result Apply(
        Invoice invoice,
        InvoicePaymentSource source,
        string reference,
        long cents
    ) =>
        invoice.ApplyPayment(
            source,
            reference,
            Money.Create(cents, "USD").Value,
            source == InvoicePaymentSource.Online ? PaymentMethod.Online : PaymentMethod.Cash,
            Now,
            ActorUserId
        );

    private static Invoice PaidInvoice(long totalCents, InvoicePaymentSource source, string reference = "pay-1")
    {
        var invoice = IssuedInvoice(totalCents);
        Assert.True(Apply(invoice, source, reference, totalCents).IsSuccess);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        return invoice;
    }

    private static Invoice IssuedInvoice(long totalCents)
    {
        var invoice = DraftInvoice(totalCents);
        Assert.True(
            invoice.Issue(InvoiceNumber.Create("INV-2026-00001").Value, Now, Now.AddDays(30), ActorUserId).IsSuccess
        );
        return invoice;
    }

    private static Invoice DraftInvoice(long totalCents)
    {
        var result = Invoice.CreateDraft(
            TenantId,
            ActorUserId,
            new CustomerSnapshot(Guid.NewGuid(), "Client", "client@example.com", null, null, null),
            "USD",
            [new DraftInvoiceLine("Tax preparation", 1, totalCents, 0)],
            null,
            Now
        );
        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
