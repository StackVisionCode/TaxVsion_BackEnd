using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.GrowthIntegrationEvents;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Growth.Api.IntegrationEvents;
using TaxVision.Referrals.Application.Attributions.CreateReferralAttribution;
using TaxVision.Referrals.Application.Attributions.FindAttributionByReferee;
using TaxVision.Referrals.Application.Programs.GetActiveReferralProgram;
using TaxVision.Referrals.Application.Qualifications.QualifyReferral;
using TaxVision.Referrals.Domain.Attributions;
using TaxVision.Referrals.Domain.Qualifications;
using TaxVision.Referrals.Domain.Rewards;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Transports.Sending;

namespace TaxVision.Growth.Tests.Application;

/// <summary>
/// El referido del alta llega por EVENTO (sin M2M de Auth): se declara el código → atribución + aviso al referidor; se
/// confirma el pago del alta → calificación. Un fallo de negocio nunca se reintenta ni bloquea el alta.
/// </summary>
public sealed class OnboardingReferralConsumersTests
{
    private static readonly Guid OnboardingId = Guid.NewGuid();

    [Fact]
    public async Task Declared_code_creates_the_attribution_under_the_onboarding_tenant_and_notifies_the_referrer()
    {
        var referrerTenant = Guid.NewGuid();
        var referrerUser = Guid.NewGuid();
        var attributionId = Guid.NewGuid();
        var bus = new ScriptedBus(message =>
            message switch
            {
                GetActiveReferralProgramQuery => Result.Success(Program()),
                CreateReferralAttributionCommand => Result.Success(
                    new CreateReferralAttributionResult(
                        attributionId,
                        ReferralAttributionStatus.Active,
                        WasReplay: false,
                        referrerTenant,
                        referrerUser,
                        "WhatsApp"
                    )
                ),
                _ => throw new InvalidOperationException(message.GetType().Name),
            }
        );

        await OnboardingReferralDeclaredConsumer.Handle(
            Declared("whatsapp"),
            bus,
            NullLogger<OnboardingReferralDeclaredIntegrationEvent>.Instance,
            default
        );

        var command = Assert.IsType<CreateReferralAttributionCommand>(bus.Invoked[1]);
        Assert.Equal(OnboardingId, command.TenantId);
        Assert.Equal(OnboardingId, command.RefereeId);
        Assert.Equal($"onboarding-referral:{OnboardingId:N}", command.IdempotencyKey);
        // El tenant del bus se restaura al terminar (no se queda como el del alta).
        Assert.Null(bus.TenantId);
        Assert.Equal(OnboardingId.ToString(), bus.TenantIdDuring[1]);

        var notice = Assert.Single(bus.Published.OfType<ReferralAttributedIntegrationEvent>());
        Assert.Equal(referrerTenant, notice.TenantId);
        Assert.Equal(attributionId, notice.AttributionId);
        Assert.Equal(referrerUser, notice.ReferrerUserId);
    }

    [Fact]
    public async Task A_replayed_declaration_does_not_notify_the_referrer_twice()
    {
        var bus = new ScriptedBus(message =>
            message switch
            {
                GetActiveReferralProgramQuery => Result.Success(Program()),
                _ => Result.Success(
                    new CreateReferralAttributionResult(
                        Guid.NewGuid(),
                        ReferralAttributionStatus.Active,
                        WasReplay: true,
                        Guid.NewGuid()
                    )
                ),
            }
        );

        await OnboardingReferralDeclaredConsumer.Handle(
            Declared(null),
            bus,
            NullLogger<OnboardingReferralDeclaredIntegrationEvent>.Instance,
            default
        );

        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task An_invalid_code_is_dropped_without_throwing_so_the_signup_is_never_blocked()
    {
        var bus = new ScriptedBus(message =>
            message switch
            {
                GetActiveReferralProgramQuery => Result.Success(Program()),
                _ => Result.Failure<CreateReferralAttributionResult>(
                    new Error("Growth.Attribution.CodeNotFound", "Revoked.")
                ),
            }
        );

        await OnboardingReferralDeclaredConsumer.Handle(
            Declared(null),
            bus,
            NullLogger<OnboardingReferralDeclaredIntegrationEvent>.Instance,
            default
        );

        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Without_an_active_program_the_declaration_is_dropped()
    {
        var bus = new ScriptedBus(_ =>
            Result.Failure<ReferralProgramTermsResult>(new Error("ReferralProgram.NotFound", "None."))
        );

        await OnboardingReferralDeclaredConsumer.Handle(
            Declared(null),
            bus,
            NullLogger<OnboardingReferralDeclaredIntegrationEvent>.Instance,
            default
        );

        Assert.Single(bus.Invoked); // solo se preguntó por el programa
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task A_paid_signup_with_an_attribution_is_qualified_idempotently_by_the_event_id()
    {
        var attributionId = Guid.NewGuid();
        var paid = Paid(amountCents: 12_900);
        var bus = new ScriptedBus(message =>
            message switch
            {
                FindAttributionByRefereeQuery => Result.Success<Guid?>(attributionId),
                QualifyReferralCommand => Result.Success(
                    new QualifyReferralResult(
                        Guid.NewGuid(),
                        ReferralQualificationDecision.Qualified,
                        null,
                        Guid.NewGuid(),
                        WasReplay: false
                    )
                ),
                _ => throw new InvalidOperationException(message.GetType().Name),
            }
        );

        await OnboardingPaymentQualifiesReferralConsumer.Handle(
            paid,
            bus,
            NullLogger<OnboardingPaymentSucceededIntegrationEvent>.Instance,
            default
        );

        var find = Assert.IsType<FindAttributionByRefereeQuery>(bus.Invoked[0]);
        Assert.Equal(OnboardingId, find.OwnerTenantId);
        Assert.Equal(OnboardingId, find.RefereeId);
        var qualify = Assert.IsType<QualifyReferralCommand>(bus.Invoked[1]);
        Assert.Equal(attributionId, qualify.AttributionId);
        Assert.Equal(paid.SaaSPaymentId, qualify.PaymentId);
        Assert.Equal(12_900, qualify.PaymentAmountCents);
        Assert.True(qualify.IsFirstSuccessfulPayment);
        Assert.Equal($"event:{paid.EventId:N}", qualify.IdempotencyKey);
        Assert.Null(bus.TenantId);
    }

    [Fact]
    public async Task A_buyer_who_did_not_arrive_by_referral_is_ignored()
    {
        var bus = new ScriptedBus(_ => Result.Success<Guid?>(null));

        await OnboardingPaymentQualifiesReferralConsumer.Handle(
            Paid(12_900),
            bus,
            NullLogger<OnboardingPaymentSucceededIntegrationEvent>.Instance,
            default
        );

        Assert.Single(bus.Invoked); // solo la búsqueda; no se calificó nada
    }

    [Fact]
    public async Task A_zero_amount_signup_is_not_looked_up_at_all()
    {
        var bus = new ScriptedBus(_ => throw new InvalidOperationException("Nothing must be invoked."));

        await OnboardingPaymentQualifiesReferralConsumer.Handle(
            Paid(0),
            bus,
            NullLogger<OnboardingPaymentSucceededIntegrationEvent>.Instance,
            default
        );

        Assert.Empty(bus.Invoked);
    }

    [Fact]
    public async Task A_rejected_qualification_is_logged_not_retried()
    {
        var bus = new ScriptedBus(message =>
            message switch
            {
                FindAttributionByRefereeQuery => Result.Success<Guid?>(Guid.NewGuid()),
                _ => Result.Failure<QualifyReferralResult>(new Error("Referral.Qualification.Expired", "Late.")),
            }
        );

        var ex = await Record.ExceptionAsync(() =>
            OnboardingPaymentQualifiesReferralConsumer.Handle(
                Paid(12_900),
                bus,
                NullLogger<OnboardingPaymentSucceededIntegrationEvent>.Instance,
                default
            )
        );

        Assert.Null(ex);
    }

    private static OnboardingReferralDeclaredIntegrationEvent Declared(string? channel) =>
        new()
        {
            TenantId = Guid.Empty,
            OnboardingId = OnboardingId,
            ReferralCode = "TVR-7BBB2F5055",
            Channel = channel,
            ShareId = Guid.NewGuid(),
        };

    private static OnboardingPaymentSucceededIntegrationEvent Paid(long amountCents) =>
        new()
        {
            TenantId = Guid.Empty,
            OnboardingId = OnboardingId,
            SaaSPaymentId = Guid.NewGuid(),
            PlanId = Guid.NewGuid(),
            AmountPaidCents = amountCents,
            Currency = "USD",
            PaidAtUtc = DateTime.UtcNow,
            ProviderPaymentReference = "pi_123",
        };

    private static ReferralProgramTermsResult Program() =>
        new(Guid.NewGuid(), "TENANT-REFERRALS", 1, ReferralRewardType.WalletCredit, 1_000, 5_000, "USD", 30, 10, 30);

    /// <summary>Bus falso: responde cada comando con un guion y apunta el tenant activo en cada invocación.</summary>
    private sealed class ScriptedBus(Func<object, object> script) : IMessageBus
    {
        public string? TenantId { get; set; }

        public List<object> Invoked { get; } = [];
        public List<object> Published { get; } = [];
        public List<string?> TenantIdDuring { get; } = [];

        public Task<T> InvokeAsync<T>(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        )
        {
            Invoked.Add(message);
            TenantIdDuring.Add(TenantId);
            return Task.FromResult((T)script(message));
        }

        public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null)
        {
            Published.Add(message!);
            return ValueTask.CompletedTask;
        }

        public Task InvokeAsync(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) =>
            throw new NotImplementedException();

        public Task InvokeAsync(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public Task<T> InvokeAsync<T>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
            object message,
            CancellationToken cancellation = default
        ) => throw new NotImplementedException();

        public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default
        ) => throw new NotImplementedException();

        public Task InvokeForTenantAsync(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public Task<T> InvokeForTenantAsync<T>(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public IDestinationEndpoint EndpointFor(string endpointName) => throw new NotImplementedException();

        public IDestinationEndpoint EndpointFor(Uri uri) => throw new NotImplementedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message) => throw new NotImplementedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message, DeliveryOptions options) =>
            throw new NotImplementedException();

        public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) =>
            throw new NotImplementedException();

        public ValueTask BroadcastToTopicAsync(string topicName, object message, DeliveryOptions? options = null) =>
            throw new NotImplementedException();
    }
}
