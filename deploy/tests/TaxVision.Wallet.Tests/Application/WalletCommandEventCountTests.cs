using System.Reflection;
using BuildingBlocks.Domain;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Messaging.WalletIntegrationEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Wallet.Application.Pricing;
using TaxVision.Wallet.Application.Reservations.Commands;
using TaxVision.Wallet.Application.Wallet.Consumers;
using TaxVision.Wallet.Tests.TestDoubles;
using Wolverine;
using WalletAggregate = TaxVision.Wallet.Domain.Wallet.Wallet;

namespace TaxVision.Wallet.Tests.Application;

/// <summary>
/// Un aviso de saldo por comando: cuenta lo publicado a mano más lo que devuelven los handlers de eventos de
/// dominio. Si conviven los dos caminos, el aviso sale dos veces y esto falla. Ningún evento de dominio sale
/// por el bus.
/// </summary>
public sealed class WalletCommandEventCountTests
{
    private const string ReferenceType = "campaign-run";

    [Fact]
    public async Task ReserveFunds_EmitsExactlyOneBalanceChanged()
    {
        var fixture = new Fixture(withBalance: true);

        await fixture.RunAsync(
            typeof(ReserveFundsHandler),
            new ReserveFundsCommand(fixture.TenantId, ReferenceType, Guid.NewGuid(), new PerChannelUnits(Sms: 5))
        );

        Assert.Equal("reserve", fixture.SingleBalanceChanged().Reason);
    }

    [Fact]
    public async Task ReserveFunds_WithoutFunds_EmitsNothing()
    {
        var fixture = new Fixture(withBalance: false);

        await fixture.RunAsync(
            typeof(ReserveFundsHandler),
            new ReserveFundsCommand(fixture.TenantId, ReferenceType, Guid.NewGuid(), new PerChannelUnits(Sms: 5))
        );

        Assert.Empty(fixture.Emitted().OfType<WalletBalanceChangedIntegrationEvent>());
    }

    [Fact]
    public async Task SettleReservation_ConsumeAndRelease_EmitsExactlyOneBalanceChanged()
    {
        var fixture = new Fixture(withBalance: true);
        var referenceId = fixture.SeedOpenReservation(units: 5);

        await fixture.RunAsync(
            typeof(SettleReservationHandler),
            new SettleReservationCommand(fixture.TenantId, ReferenceType, referenceId, ConsumedUnits: 2)
        );

        Assert.Equal("settle", fixture.SingleBalanceChanged().Reason);
    }

    [Fact]
    public async Task SettleReservation_AlreadySettled_EmitsNothing()
    {
        var fixture = new Fixture(withBalance: true);
        var referenceId = fixture.SeedOpenReservation(units: 5);
        var command = new SettleReservationCommand(fixture.TenantId, ReferenceType, referenceId, ConsumedUnits: 5);
        await fixture.RunAsync(typeof(SettleReservationHandler), command);
        fixture.Reset();

        await fixture.RunAsync(typeof(SettleReservationHandler), command);

        Assert.Empty(fixture.Emitted().OfType<WalletBalanceChangedIntegrationEvent>());
    }

    [Fact]
    public async Task ExpireReservation_EmitsExactlyOneBalanceChanged()
    {
        var fixture = new Fixture(withBalance: true);
        var referenceId = fixture.SeedOpenReservation(units: 5);

        await fixture.RunAsync(
            typeof(ExpireReservationHandler),
            new ExpireReservationCommand(fixture.TenantId, ReferenceType, referenceId)
        );

        Assert.Equal("release", fixture.SingleBalanceChanged().Reason);
    }

    [Fact]
    public async Task TopUpSucceeded_EmitsExactlyOneBalanceChanged()
    {
        var fixture = new Fixture(withBalance: false);
        var topUp = WalletTopUpBuilder.Pending(fixture.TenantId, amountCents: 10_000);
        fixture.Wallets.TopUps.Add(topUp);
        var paid = new WalletTopUpPaymentSucceededIntegrationEvent
        {
            TenantId = fixture.TenantId,
            TopUpId = topUp.Id,
            SaaSPaymentId = Guid.NewGuid(),
            IdempotencyKey = $"wallet-topup:{topUp.Id:N}",
            ExternalPaymentReference = "pi_test",
            AmountCents = 10_000,
            Currency = "USD",
            PaidAtUtc = DateTime.UtcNow,
            RequestedByUserId = Guid.NewGuid(),
        };

        await fixture.RunAsync(typeof(WalletTopUpResultConsumer), paid);
        // Reentrega del mismo pago: no acredita ni avisa otra vez.
        await fixture.RunAsync(typeof(WalletTopUpResultConsumer), paid);

        var changed = fixture.SingleBalanceChanged();
        Assert.Equal("topup", changed.Reason);
        Assert.Equal(fixture.TenantId, changed.TenantId);
    }

    private sealed class Fixture
    {
        private readonly FakeReservationRepository _reservations = new();
        private readonly List<object> _emitted = [];
        private TestMessageContext _bus = new();

        public Fixture(bool withBalance)
        {
            if (withBalance)
            {
                var wallet = WalletAggregate.Create(TenantId).Value;
                wallet.Credit(10_000_000, "topup:seed");
                Wallets.Wallets.Add(wallet);
                // Lo sembrado ya está guardado: sus eventos ya salieron.
                wallet.ClearDomainEvents();
            }
        }

        public Guid TenantId { get; } = Guid.NewGuid();
        public FakeWalletRepository Wallets { get; } = new();

        public Guid SeedOpenReservation(int units)
        {
            var referenceId = Guid.NewGuid();
            var cost = units * 10_000L;
            var wallet = Wallets.Wallets.Single();
            wallet.Reserve(cost, $"wallet-reserve:{ReferenceType}:{referenceId:N}", referenceId);
            wallet.ClearDomainEvents();
            _reservations.Reservations.Add(ReservationBuilder.Open(TenantId, ReferenceType, referenceId, cost, units));
            return referenceId;
        }

        public void Reset()
        {
            _emitted.Clear();
            _bus = new TestMessageContext();
        }

        public IReadOnlyList<object> Emitted() =>
        [.. _emitted, .. _bus.Published.OfType<Envelope>().Select(e => e.Message!)];

        public WalletBalanceChangedIntegrationEvent SingleBalanceChanged()
        {
            var emitted = Emitted();
            Assert.DoesNotContain(emitted, m => m is IDomainEvent);
            return Assert.Single(emitted.OfType<WalletBalanceChangedIntegrationEvent>());
        }

        public async Task RunAsync(Type handler, object command)
        {
            var services = new List<object>
            {
                new FakePriceBookRepository(),
                _reservations,
                Wallets,
                new FakeUnitOfWork(),
                _bus,
                new FakeCorrelationContext(),
            };
            var method = handler
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == "Handle" && m.GetParameters()[0].ParameterType == command.GetType());
            var args = method
                .GetParameters()
                .Select(p =>
                    p.ParameterType == command.GetType() ? command
                    : p.ParameterType == typeof(CancellationToken) ? CancellationToken.None
                    : IsLogger(p.ParameterType)
                        ? Activator.CreateInstance(typeof(NullLogger<>).MakeGenericType(p.ParameterType.GetGenericArguments()))
                    : services.First(s => p.ParameterType.IsInstanceOfType(s))
                )
                .ToArray();
            await (Task)method.Invoke(null, args)!;

            // Lo que el commit despacharía por la cola domain-events.
            _emitted.AddRange(DomainEventReplay.Dispatch(Wallets.Wallets));
        }

        private static bool IsLogger(Type type) =>
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ILogger<>);
    }
}
