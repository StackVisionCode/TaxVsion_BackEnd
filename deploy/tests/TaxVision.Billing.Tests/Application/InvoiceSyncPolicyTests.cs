using TaxVision.Billing.Application.Invoices.EnsureInvoicePaymentLink;
using TaxVision.Billing.Application.Invoices.SyncInvoiceStock;
using TaxVision.Billing.Application.Invoices.SyncReconciliation;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Billing.Tests.Application;

/// <summary>El job de reconciliación (BIL-3b) publica para las facturas desalineadas y no para las alineadas.</summary>
public sealed class InvoiceSyncPolicyTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorUserId = Guid.NewGuid();
    private static readonly DateTime Changed = new(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = Changed.AddMinutes(30);
    private static readonly InvoiceSyncWindow Window = new(Changed.AddHours(-1), TimeSpan.FromMinutes(15));

    [Fact]
    public void NeverSynced_PublishesBothCommands()
    {
        var invoice = Issued();

        var commands = InvoiceSyncPolicy.PlanFor(invoice, Now, Window);

        Assert.Equal(invoice.Id, Assert.Single(commands.OfType<EnsureInvoicePaymentLinkCommand>()).InvoiceId);
        Assert.Equal(invoice.Id, Assert.Single(commands.OfType<SyncInvoiceStockCommand>()).InvoiceId);
        Assert.Equal(TenantId, commands.OfType<SyncInvoiceStockCommand>().Single().TenantId);
    }

    [Fact]
    public void Aligned_PublishesNothing()
    {
        var invoice = Issued();
        invoice.MarkPaymentLinkSynced(invoice.AmountDue);
        invoice.MarkStockSynced(invoice.UpdatedAtUtc);

        Assert.Empty(InvoiceSyncPolicy.PlanFor(invoice, Now, Window));
    }

    [Fact]
    public void LinkWithOldDue_RequestsLinkOnly()
    {
        var invoice = Issued();
        invoice.MarkPaymentLinkSynced(Money.Create(3_000, "USD").Value);
        invoice.MarkStockSynced(invoice.UpdatedAtUtc);

        var commands = InvoiceSyncPolicy.PlanFor(invoice, Now, Window);

        Assert.IsType<EnsureInvoicePaymentLinkCommand>(Assert.Single(commands));
    }

    [Fact]
    public void StockSyncedForOlderVersion_RequestsStockOnly()
    {
        var invoice = Issued();
        invoice.MarkPaymentLinkSynced(invoice.AmountDue);
        invoice.MarkStockSynced(invoice.UpdatedAtUtc.AddMinutes(-5));

        var commands = InvoiceSyncPolicy.PlanFor(invoice, Now, Window);

        Assert.IsType<SyncInvoiceStockCommand>(Assert.Single(commands));
    }

    [Fact]
    public void ChangedBeforeCutoff_IsLeftForTheOwner()
    {
        var invoice = Issued();
        var window = Window with { NotBeforeUtc = Changed.AddMinutes(1) };

        Assert.Empty(InvoiceSyncPolicy.PlanFor(invoice, Now, window));
    }

    [Fact]
    public void ChangedWithinGrace_IsLeftToItsOwnCommand()
    {
        var invoice = Issued();

        Assert.Empty(InvoiceSyncPolicy.PlanFor(invoice, Changed.AddMinutes(10), Window));
    }

    [Fact]
    public void Draft_PublishesNothing()
    {
        Assert.Empty(InvoiceSyncPolicy.PlanFor(Draft(), Now, Window));
    }

    [Fact]
    public void Voided_RequestsStockOnly()
    {
        var invoice = Issued();
        Assert.True(invoice.Void("wrong client", Changed, ActorUserId).IsSuccess);

        var commands = InvoiceSyncPolicy.PlanFor(invoice, Now, Window);

        Assert.IsType<SyncInvoiceStockCommand>(Assert.Single(commands));
    }

    [Fact]
    public void PaidWithoutDue_RequestsStockOnly()
    {
        var invoice = Issued();
        Assert.True(
            invoice
                .ApplyPayment(
                    InvoicePaymentSource.Manual,
                    "cash-1",
                    invoice.AmountDue,
                    PaymentMethod.Cash,
                    Changed,
                    ActorUserId
                )
                .IsSuccess
        );

        var commands = InvoiceSyncPolicy.PlanFor(invoice, Now, Window);

        Assert.IsType<SyncInvoiceStockCommand>(Assert.Single(commands));
    }

    private static Invoice Draft() =>
        Invoice
            .CreateDraft(
                TenantId,
                ActorUserId,
                new CustomerSnapshot(Guid.NewGuid(), "Client", "client@example.com", null, null, null),
                "USD",
                [new DraftInvoiceLine("Ink cartridge", 2, 2_000, 0, Guid.NewGuid())],
                null,
                Changed
            )
            .Value;

    private static Invoice Issued()
    {
        var invoice = Draft();
        Assert.True(
            invoice
                .Issue(InvoiceNumber.Create("INV-2026-00001").Value, Changed, Changed.AddDays(30), ActorUserId)
                .IsSuccess
        );
        return invoice;
    }
}
