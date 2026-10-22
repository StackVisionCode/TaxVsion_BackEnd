using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Auth.Application.Subscriptions.IntegrationEvents;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// Cada cobro automático fallido llega al admin principal con el enlace directo al pago (Account del
/// Landing), el importe con su moneda y los datos del intento.
/// </summary>
public sealed class RenewalChargeFailedEmailConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static RenewalChargeFailedIntegrationEvent Failure(bool isFinal) =>
        new()
        {
            TenantId = TenantId,
            SaaSPaymentId = Guid.NewGuid(),
            ChargeKind = RenewalChargeKinds.Plan,
            ItemDescription = "Professional plan",
            AttemptNumber = isFinal ? 4 : 2,
            MaxAttempts = 4,
            IsFinal = isFinal,
            NextRetryAtUtc = isFinal ? null : new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc),
            AmountCents = 4900,
            Currency = "usd",
            CardBrand = "visa",
            CardLast4 = "4242",
            FailureCode = "card_declined",
            Consequence = isFinal ? RenewalChargeConsequences.SubscriptionSuspended : null,
            CorrelationId = "corr-1",
        };

    private static async Task<TenantSubscriptionEmailRequestedIntegrationEvent?> Handle(
        RenewalChargeFailedIntegrationEvent evt,
        InMemoryUserRepository users
    )
    {
        var bus = new FakeMessageBus();
        await RenewalChargeFailedEmailConsumer.Handle(
            evt,
            users,
            bus,
            FixedPlatformSettings.For("taxoffix.io"),
            new FakeCorrelationContext(),
            NullLogger<RenewalChargeFailedIntegrationEvent>.Instance,
            CancellationToken.None
        );
        return bus.Published.OfType<TenantSubscriptionEmailRequestedIntegrationEvent>().SingleOrDefault();
    }

    private static InMemoryUserRepository WithOwner()
    {
        var users = new InMemoryUserRepository();
        users.Add(
            User.Register(TenantId, "Carlos", "Castillo", "owner@office.com", "hash", UserActorType.TenantAdmin).Value
        );
        return users;
    }

    [Fact]
    public async Task Sends_the_attempt_to_the_primary_admin_with_a_direct_link_to_pay()
    {
        var request = await Handle(Failure(isFinal: false), WithOwner());

        Assert.NotNull(request);
        Assert.Equal("owner@office.com", request.Email);
        Assert.Equal("Carlos", request.FirstName);
        Assert.Equal(RenewalChargeFailedIntegrationEvent.EmailReason, request.Reason);
        Assert.Equal("https://www.taxoffix.io/account/plan", request.ManageUrl);
        Assert.Equal(request.ManageUrl, request.RenewUrl);
        Assert.Equal("$49.00", request.Amount);
        Assert.Equal("4242", request.CardLast4);
        Assert.Equal(2, request.AttemptNumber);
        Assert.Equal(4, request.MaxAttempts);
        Assert.False(request.IsFinalAttempt);
        Assert.NotNull(request.NextAttemptAtUtc);
        Assert.Equal("Professional plan", request.ItemDescription);
        Assert.Equal("Active", request.Status);
    }

    [Fact]
    public async Task The_final_attempt_carries_the_suspension()
    {
        var request = await Handle(Failure(isFinal: true), WithOwner());

        Assert.NotNull(request);
        Assert.True(request.IsFinalAttempt);
        Assert.Equal(RenewalChargeConsequences.SubscriptionSuspended, request.Consequence);
        Assert.Equal("Suspended", request.Status);
    }

    [Fact]
    public async Task Without_an_admin_nothing_is_sent()
    {
        Assert.Null(await Handle(Failure(isFinal: false), new InMemoryUserRepository()));
    }
}
