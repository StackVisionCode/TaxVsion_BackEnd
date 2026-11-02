using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers.Signature;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Tests;

/// <summary>
/// Un solo correo por firmante con todos sus documentos firmados: enlaces a la página de entrega de
/// Signature (no a shares), correo confidencial, adjuntos si la oficina los pidió, y SMS si lo prefiere.
/// </summary>
public sealed class SignedDocumentsDeliveryReadyConsumerTests
{
    private const string Token = "plain-token-123";

    [Fact]
    public async Task One_confidential_email_lists_every_file_with_a_link_to_the_delivery_page()
    {
        var evt = Event(purpose: "Completed", items: 3, attachments: [Guid.NewGuid()]);
        var (gateway, scribe, sms) = (new Gateway(), new Scribe(), new Sms());

        await Handle(evt, gateway, scribe, sms);

        var email = Assert.Single(gateway.Queued);
        Assert.Equal("ana@example.com", email.To);
        Assert.Equal(SignatureTemplateCatalog.CompletedKey, email.TemplateKey);
        Assert.True(email.Confidential);
        Assert.Equal(evt.AttachmentFileIds, email.AttachmentFileIds);
        Assert.Equal("sig.request_completed.v1", scribe.EventKey);
        var links = Assert.IsAssignableFrom<IEnumerable<Dictionary<string, object?>>>(scribe.Variables["download_links"]).ToList();
        Assert.Equal(3, links.Count);
        Assert.All(links, link => Assert.StartsWith("https://office.taxproffice.com/d/plain-token-123?item=", (string)link["url"]!));
        Assert.Equal("https://office.taxproffice.com/d/plain-token-123", scribe.Variables["delivery_url"]);
        Assert.Equal(true, scribe.Variables["has_attachments"]);
        Assert.Empty(sms.Sent);
    }

    [Fact]
    public async Task An_in_progress_copy_uses_the_partial_copy_template()
    {
        var (gateway, scribe) = (new Gateway(), new Scribe());

        await Handle(Event(purpose: "PartialCopy", items: 1), gateway, scribe, new Sms());

        Assert.Equal(SignatureTemplateCatalog.PartialCopyKey, Assert.Single(gateway.Queued).TemplateKey);
        Assert.Equal("sig.partial_copy_ready.v1", scribe.EventKey);
        Assert.Equal(2, scribe.Variables["total_signers"]);
    }

    [Fact]
    public async Task A_signer_who_prefers_sms_gets_one_sms_with_the_page_link_and_no_email()
    {
        var (gateway, sms) = (new Gateway(), new Sms());

        await Handle(Event(purpose: "Completed", items: 2, preferSms: true), gateway, new Scribe(), sms);

        Assert.Empty(gateway.Queued);
        Assert.Contains("https://office.taxproffice.com/d/plain-token-123", Assert.Single(sms.Sent));
    }

    [Fact]
    public async Task A_token_bound_to_another_delivery_is_never_sent()
    {
        var evt = Event(purpose: "Completed", items: 1) with { DeliveryId = Guid.NewGuid() };
        var gateway = new Gateway();

        await Handle(evt, gateway, new Scribe(), new Sms());

        Assert.Empty(gateway.Queued);
    }

    // ------------------------------------------------------------------

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid DeliveryId = Guid.NewGuid();

    private static SignedDocumentsDeliveryReadyIntegrationEvent Event(
        string purpose,
        int items,
        IReadOnlyList<Guid>? attachments = null,
        bool preferSms = false
    ) =>
        new()
        {
            TenantId = TenantId,
            CorrelationId = "delivery",
            SignatureRequestId = Guid.NewGuid(),
            DeliveryId = DeliveryId,
            Purpose = purpose,
            Signer = new SignerContactSnapshot(
                Guid.NewGuid(),
                "ana@example.com",
                "Ana Perez",
                "En",
                1,
                null,
                preferSms ? "+15550001111" : null,
                preferSms ? "Sms" : "Email"
            ),
            Items = Enumerable.Range(1, items).Select(index => new DeliveryItemDescriptor(Guid.NewGuid(), $"Document {index}", "SignedDocument")).ToList(),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(30),
            ProtectedToken = new Codec().Protect(
                SignatureDeliveryPayloadSchemas.DeliveryLink,
                SignatureDeliveryPayloadSchemas.BindingFor(TenantId, DeliveryId),
                new SignatureDeliveryLinkPayload(Token)
            ),
            AttachmentFileIds = attachments ?? [],
            CompletedAtUtc = DateTime.UtcNow,
            TotalSignersCount = 2,
        };

    private static Task Handle(SignedDocumentsDeliveryReadyIntegrationEvent evt, Gateway gateway, Scribe scribe, Sms sms) =>
        SignedDocumentsDeliveryReadyConsumer.Handle(
            evt,
            new Codec(),
            gateway,
            scribe,
            sms,
            new AllowAllContactPolicyRepository(),
            NoPortalRecipientResolver.Instance,
            Options.Create(new PortalOptions()),
            TestPlatform.Settings,
            new Host(),
            new Correlation(),
            NullLogger<SignedDocumentsDeliveryReadyIntegrationEvent>.Instance,
            CancellationToken.None
        );

    private sealed class Codec : IProtectedPayloadCodec
    {
        public ProtectedPayload Protect<T>(string schema, ProtectedPayloadBinding binding, T content) =>
            new()
            {
                Cipher = $"{binding.TenantId:N}|{binding.IdempotencyKey}|{System.Text.Json.JsonSerializer.Serialize(content)}",
                KeyVersion = 1,
                Schema = schema,
            };

        public T Unprotect<T>(ProtectedPayload payload, string expectedSchema, ProtectedPayloadBinding expectedBinding)
        {
            var parts = payload.Cipher.Split('|', 3);
            if (
                payload.Schema != expectedSchema
                || parts[0] != expectedBinding.TenantId.ToString("N")
                || parts[1] != expectedBinding.IdempotencyKey
            )
                throw new InvalidOperationException("Binding mismatch.");
            return System.Text.Json.JsonSerializer.Deserialize<T>(parts[2])!;
        }
    }

    private sealed class Gateway : IEmailDispatchGateway
    {
        public List<EmailDispatchRequest> Queued { get; } = [];

        public Task<EmailDispatchResult> QueueEmailAsync(EmailDispatchRequest request, CancellationToken ct = default)
        {
            Queued.Add(request);
            return Task.FromResult(
                new EmailDispatchResult(Guid.NewGuid(), Guid.NewGuid(), NotificationDispatchAttemptStatus.Queued, null, null)
            );
        }
    }

    private sealed class Scribe : IScribeRenderClient
    {
        public string? EventKey { get; private set; }
        public IReadOnlyDictionary<string, object?> Variables { get; private set; } = new Dictionary<string, object?>();

        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        )
        {
            EventKey = eventKey;
            Variables = variables;
            return Task.FromResult(Result.Success(new ScribeRenderedEmail("Subject", "<p>Body</p>", "Body")));
        }

        public Task<Result<ScribeRenderedText>> RenderTextAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }

    private sealed class Sms : ISmsSender
    {
        public List<string> Sent { get; } = [];

        public Task<Result> SendAsync(string phoneNumber, string text, CancellationToken ct, string? templateKey, string? idempotencyToken)
        {
            Sent.Add(text);
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class Host : ITenantHostResolver
    {
        public Task<string?> ResolveHostAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<string?>("office.taxproffice.com");
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
