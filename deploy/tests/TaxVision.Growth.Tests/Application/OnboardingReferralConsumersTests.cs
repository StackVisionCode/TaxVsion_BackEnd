using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.GrowthIntegrationEvents;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Growth.Api.IntegrationEvents;
using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Referrals.Application.Attributions.CreateReferralAttribution;
using TaxVision.Referrals.Application.Attributions.FindAttributionByReferee;
using TaxVision.Referrals.Application.Attributions.OnboardingReferralRejections;
using TaxVision.Referrals.Application.Programs.GetActiveReferralProgram;
using TaxVision.Referrals.Application.Qualifications.QualifyReferral;
using TaxVision.Referrals.Domain.Attributions;
using TaxVision.Referrals.Domain.Qualifications;
using TaxVision.Referrals.Domain.Rewards;

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
    public async Task An_invalid_code_is_dropped_without_throwing_and_its_rejection_is_recorded()
    {
        var bus = new ScriptedBus(message =>
            message switch
            {
                GetActiveReferralProgramQuery => Result.Success(Program()),
                RecordOnboardingReferralRejectionCommand => Result.Success(),
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
        // El pago del alta lo leerá para no reintentar un referido que nunca tendrá atribución.
        var rejection = Assert.Single(bus.Invoked.OfType<RecordOnboardingReferralRejectionCommand>());
        Assert.Equal(OnboardingId, rejection.OnboardingId);
        Assert.Equal("Growth.Attribution.CodeNotFound", rejection.ReasonCode);
        Assert.Equal(OnboardingId.ToString(), bus.TenantIdDuring[^1]);
    }

    [Fact]
    public async Task Without_an_active_program_the_declaration_is_dropped_and_its_rejection_is_recorded()
    {
        var bus = new ScriptedBus(message =>
            message switch
            {
                RecordOnboardingReferralRejectionCommand => Result.Success(),
                _ => Result.Failure<ReferralProgramTermsResult>(new Error("ReferralProgram.NotFound", "None.")),
            }
        );

        await OnboardingReferralDeclaredConsumer.Handle(
            Declared(null),
            bus,
            NullLogger<OnboardingReferralDeclaredIntegrationEvent>.Instance,
            default
        );

        Assert.Equal(2, bus.Invoked.Count); // el programa y el rechazo; ninguna atribución
        Assert.Equal(
            "ReferralProgram.NotFound",
            Assert.IsType<RecordOnboardingReferralRejectionCommand>(bus.Invoked[1]).ReasonCode
        );
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
    public async Task A_declared_referral_without_attribution_is_retried_not_dropped()
    {
        var bus = new ScriptedBus(message =>
            message switch
            {
                FindAttributionByRefereeQuery => Result.Success<Guid?>(null),
                HasOnboardingReferralRejectionQuery => Result.Success(false),
                _ => throw new InvalidOperationException(message.GetType().Name),
            }
        );

        // La declaración sigue en camino: BB-7 reintenta con espera larga y al final va a la DLQ, donde se ve.
        await Assert.ThrowsAsync<DependencyNotReadyException>(() =>
            OnboardingPaymentQualifiesReferralConsumer.Handle(
                Paid(12_900, referralDeclared: true),
                bus,
                NullLogger<OnboardingPaymentSucceededIntegrationEvent>.Instance,
                default
            )
        );
        Assert.Equal(OnboardingId.ToString(), bus.TenantIdDuring[1]);
    }

    [Fact]
    public async Task A_declared_referral_whose_code_was_rejected_is_not_retried()
    {
        var bus = new ScriptedBus(message =>
            message switch
            {
                FindAttributionByRefereeQuery => Result.Success<Guid?>(null),
                HasOnboardingReferralRejectionQuery => Result.Success(true),
                _ => throw new InvalidOperationException(message.GetType().Name),
            }
        );

        var ex = await Record.ExceptionAsync(() =>
            OnboardingPaymentQualifiesReferralConsumer.Handle(
                Paid(12_900, referralDeclared: true),
                bus,
                NullLogger<OnboardingPaymentSucceededIntegrationEvent>.Instance,
                default
            )
        );

        // El alta pagó precio completo; no hay nada que calificar ni que reintentar.
        Assert.Null(ex);
        Assert.Equal(2, bus.Invoked.Count);
    }

    [Fact]
    public async Task A_payment_without_referral_is_ignored()
    {
        var bus = new ScriptedBus(_ => Result.Success<Guid?>(null));

        await OnboardingPaymentQualifiesReferralConsumer.Handle(
            Paid(12_900, referralDeclared: false),
            bus,
            NullLogger<OnboardingPaymentSucceededIntegrationEvent>.Instance,
            default
        );

        Assert.Single(bus.Invoked); // solo la búsqueda de atribución
    }

    [Fact]
    public async Task An_old_event_without_the_flag_keeps_the_current_behavior()
    {
        var bus = new ScriptedBus(_ => Result.Success<Guid?>(null));

        await OnboardingPaymentQualifiesReferralConsumer.Handle(
            Paid(12_900, referralDeclared: null),
            bus,
            NullLogger<OnboardingPaymentSucceededIntegrationEvent>.Instance,
            default
        );

        Assert.Single(bus.Invoked);
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

    private static OnboardingPaymentSucceededIntegrationEvent Paid(long amountCents, bool? referralDeclared = null) =>
        new()
        {
            ReferralDeclared = referralDeclared,
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
}
