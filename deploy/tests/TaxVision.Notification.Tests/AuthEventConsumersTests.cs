using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Recipients;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

// El SMS del código sale en el idioma del usuario; sin idioma, en inglés (decisión del dueño).
public sealed class AuthEventConsumersTests
{
    [Fact]
    public async Task Mfa_sms_follows_the_user_language()
    {
        var sms = new NoOpSmsSender();

        await SendCodeAsync(sms, new FixedRecipientLocale("es"));

        Assert.Equal(
            $"{TestPlatform.Settings.Current.BrandName}: tu codigo es 123456. Vence en pocos minutos. No lo compartas.",
            sms.LastText
        );
    }

    [Fact]
    public async Task Mfa_sms_defaults_to_english()
    {
        var sms = new NoOpSmsSender();

        await SendCodeAsync(sms, FixedRecipientLocale.English);

        Assert.Equal(
            $"{TestPlatform.Settings.Current.BrandName}: your code is 123456. It expires in a few minutes. Do not share it.",
            sms.LastText
        );
    }

    [Fact]
    public void Spanish_mfa_sms_uses_only_basic_characters()
    {
        Assert.True(NotificationCopyText("es").All(c => c < 128), "Spanish SMS without accents or emoji (GSM-7).");

        static string NotificationCopyText(string language) =>
            TaxVision.Notification.Domain.Catalog.NotificationCopyCatalog.TryGetSms(
                SmsText.MfaCode,
                language,
                out var text
            )
                ? text
                : throw new InvalidOperationException("Missing SMS copy.");
    }

    /// <summary>El correo del código va en el idioma y la zona del usuario, y el motivo viaja como código.</summary>
    [Fact]
    public async Task Email_is_rendered_with_the_recipient_language()
    {
        var scribe = new RecordingScribeRenderClient();

        await MfaChallengeRequestedConsumer.Handle(
            new MfaChallengeRequestedIntegrationEvent
            {
                TenantId = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                Channel = "Email",
                Destination = "ana@example.com",
                Code = "123456",
                Purpose = "email_change",
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            },
            new RecordingEmailGateway(),
            scribe,
            new NoPortalAccounts(),
            new NotificationDispatcherHarness().Dispatcher,
            new FixedRecipientLocale("es", "America/Chicago"),
            Options.Create(new PortalOptions()),
            TestPlatform.Settings,
            new NoopCorrelation(),
            CancellationToken.None
        );

        var render = Assert.Single(scribe.Renders);
        Assert.Equal("Es", render.Language);
        Assert.Equal(new ScribeReader("es", "America/Chicago"), render.Reader);
        Assert.Equal("email_change", render.Variables["reason_code"]);
        Assert.False(render.Variables.ContainsKey("reason"));
    }

    private static Task SendCodeAsync(NoOpSmsSender sms, IRecipientLocale locales) =>
        MfaChallengeRequestedConsumer.Handle(
            new MfaChallengeRequestedIntegrationEvent
            {
                TenantId = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                Channel = "Sms",
                Destination = "+15555550100",
                Code = "123456",
                Purpose = "login",
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            },
            new NoopGateway(),
            new NoScribe(),
            new NoPortalAccounts(),
            new NotificationDispatcherHarness(smsSender: sms).Dispatcher,
            locales,
            Options.Create(new PortalOptions()),
            TestPlatform.Settings,
            new NoopCorrelation(),
            CancellationToken.None
        );

    private sealed class NoScribe : IScribeRenderClient
    {
        public Task<Result<ScribeRenderedText>> RenderTextAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        ) => ScribeTestDefaults.TextUnsupported();

        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("SMS does not render with Scribe.");

        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            ScribeRenderAudience audience,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("SMS does not render with Scribe.");
    }

    private sealed class NoPortalAccounts : ICustomerPortalAccountProjectionRepository
    {
        public Task<Guid?> GetActivePortalUserIdAsync(Guid tenantId, Guid customerId, CancellationToken ct = default) =>
            Task.FromResult<Guid?>(null);

        public Task<CustomerPortalAccountProjection?> GetByCustomerAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult<CustomerPortalAccountProjection?>(null);

        public Task<CustomerPortalAccountProjection?> GetByUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult<CustomerPortalAccountProjection?>(null);

        public Task AddAsync(CustomerPortalAccountProjection projection, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class NoopGateway : IEmailDispatchGateway
    {
        public Task<EmailDispatchResult> QueueEmailAsync(
            EmailDispatchRequest request,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("SMS does not send email.");
    }

    private sealed class NoopCorrelation : ICorrelationContext
    {
        public string CorrelationId => "test";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new Scope();

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
