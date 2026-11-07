using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Sms.Application.Messages.Abstractions;
using TaxVision.Sms.Application.Messages.Commands;
using TaxVision.Sms.Domain.Messages;
using TaxVision.Sms.Domain.ValueObjects;
using TaxVision.Sms.Tests.Fakes;

namespace TaxVision.Sms.Tests.Application;

/// <summary>El handler de la cola relanza: un fallo del Wallet no puede perder el cobro de un lote ya resuelto.</summary>
public sealed class SettleSmsBatchHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Batch = Guid.NewGuid();

    private readonly FakeSmsMessageRepository _messages = new();
    private readonly FakeWalletSpendClient _wallet = new();

    private Task HandleAsync() =>
        SettleSmsBatchHandler.Handle(
            new SettleSmsBatchCommand(Tenant, Batch),
            _messages,
            _wallet,
            NullLogger<SettleSmsBatchCommand>.Instance,
            CancellationToken.None
        );

    private SmsMessage Seed(string key)
    {
        var message = SmsMessage
            .Create(
                Tenant,
                Guid.NewGuid(),
                PhoneE164.Create("+18095551234").Value,
                SmsBody.Create("hi").Value,
                key,
                "corr-1",
                Batch,
                "fake",
                "docs",
                [],
                DateTime.UtcNow,
                walletCharged: true
            )
            .Value;
        _messages.SeedForProviderMessageId(message);
        return message;
    }

    private SmsMessage SeedAccepted(string key)
    {
        var message = Seed(key);
        message.MarkSendAttempted(DateTime.UtcNow);
        message.MarkAccepted("prov-" + key, DateTime.UtcNow);
        return message;
    }

    [Fact]
    public async Task Handle_WalletFails_Rethrows()
    {
        SeedAccepted("a");
        _wallet.SettleFailures.Enqueue(new InvalidOperationException("Wallet settle failed (503)."));

        await Assert.ThrowsAsync<InvalidOperationException>(HandleAsync);
    }

    [Fact]
    public async Task Handle_Retried_ReportsTheSameReferenceAndUnits()
    {
        SeedAccepted("a");
        SeedAccepted("b");
        _wallet.SettleFailures.Enqueue(new InvalidOperationException("Wallet settle failed (503)."));

        await Assert.ThrowsAsync<InvalidOperationException>(HandleAsync);
        await HandleAsync();

        Assert.Equal(2, _wallet.Settlements.Count);
        Assert.Equal(_wallet.Settlements[0], _wallet.Settlements[1]);
        Assert.Equal(WalletReferenceTypes.IndividualSms, _wallet.Settlements[1].ReferenceType);
        Assert.Equal(Batch, _wallet.Settlements[1].ReferenceId);
        Assert.Equal(2, _wallet.Settlements[1].ConsumedUnits);
        Assert.Null(_wallet.Settlements[1].CallerBearerToken);
    }

    [Fact]
    public async Task Handle_UnresolvedRow_LeavesTheReservationOpen()
    {
        SeedAccepted("a");
        Seed("pending");

        await HandleAsync();

        Assert.Empty(_wallet.Settlements);
    }
}
