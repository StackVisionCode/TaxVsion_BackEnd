using BuildingBlocks.Common;
using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Tests;

/// <summary>
/// La factura que la oficina manda a su cliente. Lo que importa: que se renderice por el EVENT key
/// (Scribe resuelve la plantilla desde el mapeo, no desde el nombre de la plantilla), que salga por el
/// carril de la oficina (<c>TenantPreferred</c>, que además marca el logo como del tenant) y que el PDF
/// viaje adjunto.
/// </summary>
public sealed class InvoiceSentConsumerTests
{
    private static readonly Guid TenantId = Guid.Parse("d4879234-7370-4b58-b49c-094bd7c04847");

    [Fact]
    public async Task Renderiza_por_event_key_y_encola_por_el_carril_de_la_oficina()
    {
        var render = new RecordingRenderClient();
        var gateway = new RecordingEmailGateway();
        var pdf = Guid.NewGuid();

        await Handle(render, gateway, Invoice("client@example.com", pdf));

        Assert.Equal("billing.invoice_sent.v1", render.LastEventKey);
        Assert.Equal("100.00 USD", render.LastVariables["amount_due"]);

        var email = Assert.Single(gateway.Queued);
        Assert.Equal("client@example.com", email.To);
        Assert.Equal("billing.invoice_sent", email.TemplateKey);
        Assert.Equal(EmailDispatchScope.TenantPreferred, email.Scope);
        Assert.Equal([pdf], email.AttachmentFileIds);
    }

    [Fact]
    public async Task Sin_email_del_cliente_no_se_renderiza_ni_se_encola()
    {
        var render = new RecordingRenderClient();
        var gateway = new RecordingEmailGateway();

        await Handle(render, gateway, Invoice(email: "", pdfFileId: null));

        Assert.Equal(0, render.Calls);
        Assert.Empty(gateway.Queued);
    }

    private static Task Handle(
        RecordingRenderClient render,
        RecordingEmailGateway gateway,
        InvoiceSentToCustomerIntegrationEvent evt
    ) => InvoiceSentConsumer.Handle(evt, gateway, render, new NoOpCorrelationContext(), CancellationToken.None);

    private static InvoiceSentToCustomerIntegrationEvent Invoice(string email, Guid? pdfFileId) =>
        new()
        {
            TenantId = TenantId,
            CorrelationId = "test",
            InvoiceId = Guid.NewGuid(),
            InvoiceNumber = "INV-2026-00005",
            CustomerEmail = email,
            CustomerName = "Client",
            AmountDueCents = 10000,
            Currency = "usd",
            DueDateUtc = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc),
            PaymentLink = "https://manfer.taxproffice.com/payments-client/invoices/abc",
            PdfFileId = pdfFileId,
        };

    private sealed class RecordingRenderClient : IScribeRenderClient
    {
        public IReadOnlyDictionary<string, object?> LastVariables { get; private set; } =
            new Dictionary<string, object?>();
        public string? LastEventKey { get; private set; }
        public int Calls { get; private set; }

        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        )
        {
            Calls++;
            LastEventKey = eventKey;
            LastVariables = variables;
            // Lo que devolveria Scribe para una plantilla sobre tenant-base: el carril viene del render.
            return Task.FromResult(
                Result.Success(
                    new ScribeRenderedEmail("subject", "<p>html</p>", "text", [], EmailDispatchScope.TenantPreferred)
                )
            );
        }
    }

    private sealed class RecordingEmailGateway : IEmailDispatchGateway
    {
        public List<EmailDispatchRequest> Queued { get; } = [];

        public Task<EmailDispatchResult> QueueEmailAsync(EmailDispatchRequest request, CancellationToken ct = default)
        {
            Queued.Add(request);
            return Task.FromResult(
                new EmailDispatchResult(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    NotificationDispatchAttemptStatus.Sent,
                    null,
                    null
                )
            );
        }
    }

    private sealed class NoOpCorrelationContext : ICorrelationContext
    {
        public string CorrelationId => "test";

        public IDisposable Push(string correlationId) => new Noop();

        public void Set(string correlationId) { }

        private sealed class Noop : IDisposable
        {
            public void Dispose() { }
        }
    }
}
