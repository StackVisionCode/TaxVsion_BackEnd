using BuildingBlocks.Messaging.BillingIntegrationEvents;
using TaxVision.Tenant.Application.Team.Consumers;
using TaxVision.Tenant.Domain.Team;
using TaxVision.Tenant.Tests.TestSupport;

namespace TaxVision.Tenant.Tests.Application.Team;

/// <summary>Las facturas quedan Restricted: verlas en la actividad exige <c>invoicing.view</c>.</summary>
public sealed class BillingActivityConsumerTests : ActivityConsumerContract
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid InvoiceId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();

    protected override IEnumerable<ActivityCase> Cases()
    {
        var issued = Stamp(
            new InvoiceIssuedIntegrationEvent
            {
                InvoiceId = InvoiceId,
                InvoiceNumber = "INV-1",
                CustomerId = CustomerId,
                TotalAmountCents = 10_000,
                Currency = "USD",
                DueDateUtc = DateTime.UtcNow,
                IssuedByUserId = Actor,
            }
        );
        yield return new(
            issued,
            issued with
            {
                IssuedByUserId = null,
            },
            (e, r, u, c) => BillingActivityConsumer.Handle((InvoiceIssuedIntegrationEvent)e, r, u, c, default),
            Actor,
            ActivityVerbs.InvoiceIssued,
            InvoiceId,
            CustomerId,
            ActivityVisibility.Restricted
        );

        // Un cobro en línea no tiene quién lo registró: no es acción de un miembro.
        var paid = Stamp(
            new InvoicePaidIntegrationEvent
            {
                InvoiceId = InvoiceId,
                InvoiceNumber = "INV-1",
                CustomerId = CustomerId,
                AmountPaidCents = 10_000,
                Currency = "USD",
                PaymentMethod = "Cash",
                PaidAtUtc = DateTime.UtcNow,
                RecordedByUserId = Actor,
            }
        );
        yield return new(
            paid,
            paid with
            {
                RecordedByUserId = null,
            },
            (e, r, u, c) => BillingActivityConsumer.Handle((InvoicePaidIntegrationEvent)e, r, u, c, default),
            Actor,
            ActivityVerbs.InvoicePaymentRecorded,
            InvoiceId,
            CustomerId,
            ActivityVisibility.Restricted
        );

        var voided = Stamp(
            new InvoiceVoidedIntegrationEvent
            {
                InvoiceId = InvoiceId,
                InvoiceNumber = "INV-1",
                VoidedByUserId = Actor,
                CustomerId = CustomerId,
            }
        );
        yield return new(
            voided,
            voided with
            {
                VoidedByUserId = null,
            },
            (e, r, u, c) => BillingActivityConsumer.Handle((InvoiceVoidedIntegrationEvent)e, r, u, c, default),
            Actor,
            ActivityVerbs.InvoiceVoided,
            InvoiceId,
            CustomerId,
            ActivityVisibility.Restricted
        );

        var sent = Stamp(
            new InvoiceSentToCustomerIntegrationEvent
            {
                InvoiceId = InvoiceId,
                InvoiceNumber = "INV-1",
                CustomerEmail = "client@mail.test",
                CustomerName = "Client",
                AmountDueCents = 10_000,
                Currency = "USD",
                SentByUserId = Actor,
            }
        );
        yield return new(
            sent,
            sent with
            {
                SentByUserId = null,
            },
            (e, r, u, c) => BillingActivityConsumer.Handle((InvoiceSentToCustomerIntegrationEvent)e, r, u, c, default),
            Actor,
            ActivityVerbs.InvoiceSent,
            InvoiceId,
            null,
            ActivityVisibility.Restricted
        );
    }
}
