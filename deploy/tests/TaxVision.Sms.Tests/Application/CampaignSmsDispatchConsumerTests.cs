using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Sms.Application.Messages.Commands;
using TaxVision.Sms.Application.Messages.Consumers;
using TaxVision.Sms.Application.Providers;
using TaxVision.Sms.Tests.Fakes;

namespace TaxVision.Sms.Tests.Application;

/// <summary>
/// El resultado del dispatch de campaña: un SMS que todavía puede salir (Pending u OutcomeUnknown) no se
/// reporta como Failed. Va como Unknown y la conciliación lo cierra con SmsMessageDelivered/Failed.
/// </summary>
public sealed class CampaignSmsDispatchConsumerTests
{
    private static CampaignDispatchRequestedIntegrationEvent Dispatch() =>
        new()
        {
            TenantId = Guid.NewGuid(),
            CorrelationId = "corr",
            CampaignId = Guid.NewGuid(),
            RunId = Guid.NewGuid(),
            RecipientId = Guid.NewGuid(),
            AttemptNo = 1,
            DispatchId = "d-1",
            Channel = "Sms",
            ContactRef = Guid.NewGuid().ToString(),
            PhoneE164 = "+18095550000",
            Body = "hola",
        };

    private static FakeMessageBus BusReturning(string status, string? errorCode)
    {
        var bus = new FakeMessageBus();
        bus.OnInvoke = message =>
        {
            var command = (SendSmsBatchCommand)message;
            var item = command.Items[0];
            var result = new SmsSendItemResult(Guid.NewGuid(), item.CustomerId, item.To, status, null, errorCode);
            return Task.FromResult<object?>(
                Result.Success(new SendSmsBatchResponse(Guid.NewGuid(), command.CorrelationId, [result]))
            );
        };
        return bus;
    }

    [Theory]
    [InlineData("OutcomeUnknown", SmsSendFailure.OutcomeUnknown)]
    [InlineData("Pending", null)]
    public async Task Item_that_may_still_be_sent_is_reported_as_unknown(string status, string? errorCode)
    {
        var result = await CampaignSmsDispatchConsumer.Handle(
            Dispatch(),
            BusReturning(status, errorCode),
            CancellationToken.None
        );

        Assert.NotNull(result);
        Assert.Equal("Unknown", result!.Outcome);
    }

    [Theory]
    [InlineData("Accepted", "Accepted")]
    [InlineData("Suppressed", "Skipped")]
    [InlineData("Failed", "Failed")]
    public async Task Final_item_states_keep_their_outcome(string status, string expected)
    {
        var result = await CampaignSmsDispatchConsumer.Handle(
            Dispatch(),
            BusReturning(status, null),
            CancellationToken.None
        );

        Assert.Equal(expected, result!.Outcome);
    }

    [Fact]
    public async Task Campaign_send_enforces_the_contact_policy_and_reports_skipped()
    {
        var bus = BusReturning("Suppressed", "contact_policy");
        SendSmsBatchCommand? sent = null;
        var inner = bus.OnInvoke!;
        bus.OnInvoke = message =>
        {
            sent = (SendSmsBatchCommand)message;
            return inner(message);
        };

        var result = await CampaignSmsDispatchConsumer.Handle(Dispatch(), bus, CancellationToken.None);

        Assert.True(sent!.EnforceContactPolicy);
        Assert.False(sent.ChargeWallet);
        Assert.Equal("Skipped", result!.Outcome);
        Assert.Equal("contact_policy", result.Reason);
    }
}
