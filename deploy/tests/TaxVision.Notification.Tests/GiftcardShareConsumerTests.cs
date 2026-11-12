using BuildingBlocks.Common;
using BuildingBlocks.Messaging.GrowthIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Tests;

/// <summary>
/// El PlatformAdmin envía una giftcard por correo: Growth publica el evento y Notification renderiza la plantilla de
/// Scribe y despacha el email al destinatario (sin llamadas M2M entre servicios).
/// </summary>
public sealed class GiftcardShareConsumerTests
{
    private static GiftcardShareRequestedIntegrationEvent Event(
        string? name = "Ana",
        string? message = "Enjoy!",
        DateTime? expires = null
    ) =>
        new()
        {
            TenantId = Guid.NewGuid(),
            AggregateId = Guid.NewGuid(),
            RecipientEmail = "ana@example.com",
            RecipientName = name,
            ValueLabel = "20% OFF",
            Code = "GIFT-7K3Q9M2X",
            ExpiresAtUtc = expires,
            PersonalMessage = message,
            RedeemUrl = "https://taxoffix.io/register?gift=GIFT-7K3Q9M2X",
        };

    private static async Task<(Capturing Scribe, Gateway Gateway)> Run(GiftcardShareRequestedIntegrationEvent evt)
    {
        var scribe = new Capturing();
        var gateway = new Gateway();
        await GiftcardShareConsumer.Handle(evt, gateway, scribe, new Correlation(), CancellationToken.None);
        return (scribe, gateway);
    }

    [Fact]
    public async Task It_renders_the_giftcard_template_with_the_card_data_and_sends_it_to_the_recipient()
    {
        var (scribe, gateway) = await Run(Event(expires: new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc)));

        var render = Assert.Single(scribe.Rendered);
        Assert.Equal("giftcards.share_requested.v1", render.EventKey);
        Assert.Equal("Ana", render.Variables["recipient_name"]);
        Assert.Equal("20% OFF", render.Variables["value_label"]);
        Assert.Equal("GIFT-7K3Q9M2X", render.Variables["code"]);
        Assert.Equal("Valid until Dec 31, 2026", render.Variables["expires_text"]);
        Assert.Equal("https://taxoffix.io/register?gift=GIFT-7K3Q9M2X", render.Variables["redeem_url"]);
        Assert.Equal("Enjoy!", render.Variables["personal_message"]);
        var sent = Assert.Single(gateway.Requests);
        Assert.Equal("ana@example.com", sent.To);
        Assert.Equal("giftcards.share", sent.TemplateKey);
    }

    [Fact]
    public async Task Without_a_name_it_uses_the_local_part_of_the_email_and_without_expiry_says_so()
    {
        var (scribe, _) = await Run(Event(name: null, message: null));

        var variables = Assert.Single(scribe.Rendered).Variables;
        Assert.Equal("ana", variables["recipient_name"]);
        Assert.Equal("No expiry", variables["expires_text"]);
        Assert.Equal(string.Empty, variables["personal_message"]);
    }

    private sealed record Render(string EventKey, IReadOnlyDictionary<string, object?> Variables);

    private sealed class Capturing : IScribeRenderClient
    {
        public List<Render> Rendered { get; } = [];

        public Task<Result<ScribeRenderedText>> RenderTextAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        ) => TestDoubles.ScribeTestDefaults.TextUnsupported();

        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        ) => RenderAsync(eventKey, tenantId, variables, ScribeRenderAudience.Staff, ct);

        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            ScribeRenderAudience audience,
            CancellationToken ct = default
        )
        {
            Rendered.Add(new Render(eventKey, variables));
            return Task.FromResult(Result.Success(new ScribeRenderedEmail("subject", "<p>body</p>", "body")));
        }
    }

    private sealed class Gateway : IEmailDispatchGateway
    {
        public List<EmailDispatchRequest> Requests { get; } = [];

        public Task<EmailDispatchResult> QueueEmailAsync(EmailDispatchRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(
                new EmailDispatchResult(Guid.NewGuid(), Guid.NewGuid(), NotificationDispatchAttemptStatus.Sent, null, null)
            );
        }
    }

    private sealed class Correlation : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = "test";

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId)
        {
            CorrelationId = correlationId;
            return new Scope();
        }

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
