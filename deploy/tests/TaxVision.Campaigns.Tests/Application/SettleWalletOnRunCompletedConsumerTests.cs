using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using TaxVision.Campaigns.Application.Runs.Consumers;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Runs;
using TaxVision.Campaigns.Tests.Fakes;

namespace TaxVision.Campaigns.Tests.Application;

public sealed class SettleWalletOnRunCompletedConsumerTests
{
    private readonly FakeCampaignRunRepository _runs = new();
    private readonly FakeWalletSpendClient _wallet = new();

    private Task HandleAsync(Guid tenantId, Guid runId) =>
        SettleWalletOnRunCompletedConsumer.Handle(
            new CampaignRunCompletedIntegrationEvent
            {
                TenantId = tenantId,
                CampaignId = Guid.NewGuid(),
                RunId = runId,
                TerminalStatus = nameof(CampaignRunStatus.Completed),
                RecipientCount = 1,
                Delivered = 1,
                Accepted = 0,
                Failed = 0,
                Skipped = 0,
                Unknown = 0,
            },
            _runs,
            _wallet,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

    [Fact]
    public async Task Handle_RunNotFound_WaitsForTheRunInsteadOfDropping()
    {
        await Assert.ThrowsAsync<DependencyNotReadyException>(() => HandleAsync(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Empty(_wallet.Settlements);
    }

    [Fact]
    public async Task Handle_RunFound_SettlesTheNonSkippedBillableUnits()
    {
        var tenantId = Guid.NewGuid();
        var run = CampaignRun
            .Start(
                tenantId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Manual",
                [
                    new RunRecipientDraft("a", CampaignChannel.Sms, null, "+15550000001"),
                    new RunRecipientDraft("b", CampaignChannel.Sms, null, null),
                ]
            )
            .Value;
        _runs.Runs.Add(run);

        await HandleAsync(tenantId, run.Id);

        var settled = Assert.Single(_wallet.Settlements);
        Assert.Equal(run.Id, settled.ReferenceId);
        Assert.Equal(1, settled.ConsumedUnits);
    }
}
