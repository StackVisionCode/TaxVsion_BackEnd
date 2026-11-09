extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;

namespace TaxVision.Campaigns.Tests.Architecture;

// CMP-15: los jobs de Campaigns solo invocan un comando por entidad; no guardan ni publican por su cuenta.
public sealed class CampaignsJobPublishingTests : JobPublishingRulesBase
{
    protected override string SourceDirectory => "src/Services/Campaigns";

    [Theory]
    [InlineData("CampaignSchedulerService.cs")]
    [InlineData("CampaignRunSweepJob.cs")]
    public void Job_OnlyInvokesPerEntity(string file) =>
        Assert.Equal(
            JobPublishingKind.InvokesPerEntity,
            Assert
                .Single(JobPublishingRules.Scan(SourceDirectory), f => f.Path.EndsWith(file, StringComparison.Ordinal))
                .Kind
        );
}
