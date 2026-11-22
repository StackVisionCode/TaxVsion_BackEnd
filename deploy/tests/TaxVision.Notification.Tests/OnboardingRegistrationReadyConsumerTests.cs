using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Onboarding;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>El correo del alta dice lo pagado y, si hubo códigos, el precio de lista y el descuento.</summary>
public sealed class OnboardingRegistrationReadyConsumerTests
{
    [Fact]
    public async Task A_discounted_signup_sends_the_list_price_and_the_discount()
    {
        var scribe = new CapturingScribeRenderClient();

        await HandleAsync(
            Event() with
            {
                GrossFormatted = "64.00 USD",
                DiscountFormatted = "15.00 USD",
                DiscountSummary = "Referral discount + Promo code SPRI...0420",
            },
            scribe
        );

        Assert.Equal("64.00 USD", scribe.LastVariables!["gross_formatted"]);
        Assert.Equal("15.00 USD", scribe.LastVariables["discount_formatted"]);
        Assert.Equal("Referral discount + Promo code SPRI...0420", scribe.LastVariables["discount_summary"]);
    }

    [Fact]
    public async Task A_signup_without_codes_sends_empty_discount_fields()
    {
        var scribe = new CapturingScribeRenderClient();

        await HandleAsync(Event(), scribe);

        Assert.Equal(string.Empty, scribe.LastVariables!["discount_formatted"]);
    }

    private static OnboardingRegistrationReadyIntegrationEvent Event() =>
        new()
        {
            OnboardingId = Guid.NewGuid(),
            TokenReference = Guid.NewGuid(),
            Email = "ada@example.com",
            FirstName = "Ada",
            PlanName = "Professional",
            PriceFormatted = "49.00 USD",
            PaidAtUtc = new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc),
            RegistrationUrlBase = "https://app.example.com/register",
        };

    private static Task HandleAsync(
        OnboardingRegistrationReadyIntegrationEvent evt,
        CapturingScribeRenderClient scribe
    ) =>
        OnboardingRegistrationReadyConsumer.Handle(
            evt,
            new FixedTokenClient(),
            new NoReceipts(),
            new RecordingEmailDispatchGateway(),
            scribe,
            Options.Create(new PortalOptions()),
            FixedRecipientLocale.English,
            new NoOpCorrelationContext(),
            NullLogger<OnboardingRegistrationReadyIntegrationEvent>.Instance,
            CancellationToken.None
        );

    private sealed class FixedTokenClient : IOnboardingTokenClient
    {
        public Task<Result<string>> ResolveRegistrationUrlAsync(Guid tokenReference, CancellationToken ct = default) =>
            Task.FromResult(Result.Success("https://app.example.com/register?token=x"));
    }

    private sealed class NoReceipts : IOnboardingReceiptLookupRepository
    {
        public Task<OnboardingReceiptLookup?> GetByOnboardingIdAsync(
            Guid onboardingId,
            CancellationToken ct = default
        ) => Task.FromResult<OnboardingReceiptLookup?>(null);

        public Task AddAsync(OnboardingReceiptLookup lookup, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoOpCorrelationContext : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = string.Empty;

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId)
        {
            CorrelationId = correlationId;
            return new NoOpDisposable();
        }

        private sealed class NoOpDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class CapturingScribeRenderClient : IScribeRenderClient
    {
        public IReadOnlyDictionary<string, object?>? LastVariables { get; private set; }

        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            ScribeRenderAudience audience,
            CancellationToken ct = default
        ) => RenderAsync(eventKey, tenantId, variables, ct);

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
        )
        {
            LastVariables = variables;
            return Task.FromResult(Result.Success(new ScribeRenderedEmail("Subject", "<p>Body</p>", null)));
        }
    }

    private sealed class RecordingEmailDispatchGateway : IEmailDispatchGateway
    {
        public Task<EmailDispatchResult> QueueEmailAsync(
            EmailDispatchRequest request,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                new EmailDispatchResult(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    NotificationDispatchAttemptStatus.Queued,
                    null,
                    null
                )
            );
    }
}
