using System.Net;
using System.Text;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Infrastructure.Scribe;

namespace TaxVision.Notification.Tests;

/// <summary>
/// El carril de envío lo decide el layout en Scribe y tiene que llegar entero hasta el evento. Son
/// cuatro saltos y cada uno es un sitio donde el dato se cae sin ruido:
/// <c>RenderedContent</c> → JSON de <c>/scribe/render</c> → <c>RenderResponseDto</c> →
/// <c>ScribeRenderedEmail</c> → <c>EmailDispatchRequest</c>.
///
/// <para>Existe por lo que ya pasó una vez: <c>InlineAssets</c> no estaba declarado en el DTO del
/// cliente, <c>System.Text.Json</c> lo descartó en silencio y ningún logo llegó nunca más allá de ese
/// deserializador — con todo compilando y todos los tests en verde.</para>
/// </summary>
public sealed class DispatchScopeFourHopsTests
{
    [Fact]
    public async Task The_lane_survives_the_json_hop()
    {
        // El salto que no cubre ningún test de tipos: lo que Scribe serializa tiene que tener un
        // miembro donde caer en el DTO del cliente.
        var client = ClientReturning(
            """{"subject":"S","html":"<p>H</p>","text":"H","inlineAssets":[],"dispatchScope":"TenantPreferred"}"""
        );

        var result = await client.RenderAsync(
            "billing.invoice_sent.v1",
            Guid.NewGuid(),
            new Dictionary<string, object?>()
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(EmailDispatchScope.TenantPreferred, result.Value.DispatchScope);
    }

    [Theory]
    [InlineData("\"dispatchScope\":\"Potato\"")]
    [InlineData("\"dispatchScope\":null")]
    [InlineData("\"unrelated\":1")]
    public async Task An_unreadable_lane_falls_back_to_System(string scopeFragment)
    {
        // Un valor que no se entiende no puede terminar mandando el correo por el buzón de una oficina.
        var client = ClientReturning($$"""{"subject":"S","html":"<p>H</p>","text":"H",{{scopeFragment}}}""");

        var result = await client.RenderAsync(
            "auth.password_reset.v1",
            Guid.NewGuid(),
            new Dictionary<string, object?>()
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(EmailDispatchScope.System, result.Value.DispatchScope);
    }

    [Fact]
    public async Task The_consumer_forwards_the_lane_instead_of_choosing_one()
    {
        var gateway = new RecordingGateway();
        var render = new ScribeRenderedEmail("S", "<p>H</p>", "H", [], EmailDispatchScope.TenantPreferred);

        await InvoiceSentConsumer.Handle(
            Invoice(),
            gateway,
            new StubRenderClient(render),
            new NoOpCorrelation(),
            CancellationToken.None
        );

        var queued = Assert.Single(gateway.Queued);
        Assert.Equal(EmailDispatchScope.TenantPreferred, queued.Scope);
    }

    [Fact]
    public async Task A_platform_render_keeps_the_consumer_on_the_system_lane()
    {
        // Mismo consumer, carril distinto: la decisión no vive acá.
        var gateway = new RecordingGateway();
        var render = new ScribeRenderedEmail("S", "<p>H</p>", "H", [], EmailDispatchScope.System);

        await InvoiceSentConsumer.Handle(
            Invoice(),
            gateway,
            new StubRenderClient(render),
            new NoOpCorrelation(),
            CancellationToken.None
        );

        Assert.Equal(EmailDispatchScope.System, Assert.Single(gateway.Queued).Scope);
    }

    private static ScribeRenderClient ClientReturning(string json) =>
        new(
            new HttpClient(new StubHandler(json)) { BaseAddress = new Uri("http://scribe.test/") },
            new StubTokenAcquirer(),
            NullLogger<ScribeRenderClient>.Instance
        );

    private static InvoiceSentToCustomerIntegrationEvent Invoice() =>
        new()
        {
            TenantId = Guid.NewGuid(),
            CorrelationId = "test",
            InvoiceId = Guid.NewGuid(),
            InvoiceNumber = "INV-2026-00005",
            CustomerEmail = "client@example.com",
            CustomerName = "Client",
            AmountDueCents = 10000,
            Currency = "usd",
            DueDateUtc = null,
            PaymentLink = null,
            PdfFileId = null,
        };

    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                }
            );
    }

    private sealed class StubTokenAcquirer : IServiceTokenAcquirer
    {
        public Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<string?>("token");
    }

    private sealed class StubRenderClient(ScribeRenderedEmail render) : IScribeRenderClient
    {
        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success(render));
    }

    private sealed class RecordingGateway : IEmailDispatchGateway
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

    private sealed class NoOpCorrelation : ICorrelationContext
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
