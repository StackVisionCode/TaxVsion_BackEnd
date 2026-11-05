using BuildingBlocks.Messaging.SmsIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Sms.Application.Messages.Abstractions;
using TaxVision.Sms.Application.Messages.Commands;
using TaxVision.Sms.Application.Providers;
using TaxVision.Sms.Application.Webhooks.Commands;
using TaxVision.Sms.Domain.Messages;
using TaxVision.Sms.Domain.ValueObjects;
using TaxVision.Sms.Tests.Fakes;

namespace TaxVision.Sms.Tests.Application;

/// <summary>
/// D-SMS-3: un SMS de resultado desconocido deja su unidad apartada. La conciliación la cobra si el proveedor
/// confirma que salió y la devuelve si no llega confirmación antes del vencimiento.
/// </summary>
public sealed class SmsStatusReconciliationTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private sealed class Harness
    {
        public FakeSmsMessageRepository Messages { get; } = new();
        public FakeSmsProvider Provider { get; } =
            new() { Capabilities = FakeSmsProvider.FullCapabilities() with { SupportsStatusPull = true } };
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public FakeMessageBus Bus { get; } = new();
        public FakeWalletSpendClient Wallet { get; } = new();

        public Harness()
        {
            Bus.OnInvoke = async message =>
                message switch
                {
                    ReconcileSmsStatusesCommand reconcile => await ReconcileSmsStatusesHandler.Handle(
                        reconcile,
                        new FakeSmsAdapterFactory(Provider),
                        Messages,
                        UnitOfWork,
                        Bus,
                        NullLogger<ReconcileSmsStatusesCommand>.Instance,
                        CancellationToken.None
                    ),
                    SettleSmsBatchCommand settle => await SettleSmsBatchHandler.Handle(
                        settle,
                        Messages,
                        Wallet,
                        NullLogger<SettleSmsBatchCommand>.Instance,
                        CancellationToken.None
                    ),
                    _ => throw new NotSupportedException(message.GetType().Name),
                };
        }

        public Task Run() => SmsReconciliation.RunAsync(Bus, new ReconcileSmsStatusesCommand(), CancellationToken.None);
    }

    private static SmsMessage UnknownMessage(DateTime attemptedAtUtc, string? providerMessageId)
    {
        var message = SmsMessage
            .Create(
                Tenant,
                Guid.NewGuid(),
                PhoneE164.Create("+18095551234").Value,
                SmsBody.Create("hi").Value,
                "idem-" + Guid.NewGuid().ToString("N"),
                "corr-1",
                Guid.NewGuid(),
                "fake",
                "docs",
                [],
                attemptedAtUtc,
                walletCharged: true
            )
            .Value;
        message.MarkSendAttempted(attemptedAtUtc);
        message.MarkOutcomeUnknown(attemptedAtUtc, providerMessageId, "sms.providerOutcomeUnknown", "timeout");
        return message;
    }

    [Fact]
    public async Task UnknownThenDelivered_ConsumesUnit()
    {
        var h = new Harness();
        var message = UnknownMessage(DateTime.UtcNow.AddMinutes(-5), "prov-1");
        h.Messages.SeedForProviderMessageId(message);
        h.Provider.Reports.Add(new SmsDeliveryUpdate("prov-1", "delivered", SmsCanonicalStatus.Delivered, null, null));

        await h.Run();

        Assert.Equal(SmsMessageStatus.Delivered, message.Status);
        var settle = Assert.Single(h.Wallet.Settlements);
        Assert.Equal(WalletReferenceTypes.IndividualSms, settle.ReferenceType);
        Assert.Equal(message.BatchId, settle.ReferenceId);
        Assert.Equal(1, settle.ConsumedUnits);
    }

    [Fact]
    public async Task UnknownThenNotFound_ReleasesUnit()
    {
        var h = new Harness();
        var message = UnknownMessage(DateTime.UtcNow.AddHours(-2), providerMessageId: null);
        h.Messages.SeedForProviderMessageId(message);

        await h.Run();

        Assert.Equal(SmsMessageStatus.Failed, message.Status);
        Assert.Equal("sms.providerOutcomeUnknown", message.FailureCode);
        Assert.NotNull(h.Bus.LastOfType<SmsMessageFailedIntegrationEvent>());
        Assert.Equal(0, Assert.Single(h.Wallet.Settlements).ConsumedUnits);
    }

    [Fact]
    public async Task Pending_without_confirmation_is_closed_without_resending()
    {
        var h = new Harness();
        var message = SmsMessage
            .Create(
                Tenant,
                Guid.NewGuid(),
                PhoneE164.Create("+18095551234").Value,
                SmsBody.Create("hi").Value,
                "idem-pending",
                "corr-1",
                Guid.NewGuid(),
                "fake",
                "docs",
                [],
                DateTime.UtcNow.AddMinutes(-30)
            )
            .Value;
        message.MarkSendAttempted(DateTime.UtcNow.AddMinutes(-30));
        h.Messages.SeedForProviderMessageId(message);

        await h.Run();

        Assert.Equal(SmsMessageStatus.Failed, message.Status);
        Assert.Equal("sms.providerOutcomeUnknown", message.FailureCode);
        Assert.Equal(0, h.Provider.SendAsyncCallCount);
        Assert.Empty(h.Wallet.Settlements); // el lote no se cobró al Wallet
    }
}
