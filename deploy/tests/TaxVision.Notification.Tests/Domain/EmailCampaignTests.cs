using TaxVision.Notification.Domain.Emailing.Campaigns;

namespace TaxVision.Notification.Tests.Domain;

public sealed class EmailCampaignTests
{
    [Fact]
    public void Campaign_requires_recipients()
    {
        var result = EmailCampaign.Create(
            Guid.NewGuid(),
            "Newsletter",
            CampaignType.Newsletter,
            Guid.NewGuid(),
            [],
            null
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Campaign.Recipients", result.Error.Code);
    }

    [Fact]
    public void New_campaign_is_draft_with_total_recipients()
    {
        var campaign = CreateCampaign();

        Assert.Equal(CampaignStatus.Draft, campaign.Status);
        Assert.Equal(2, campaign.TotalRecipients);
    }

    [Fact]
    public void Only_draft_campaigns_can_be_scheduled()
    {
        var campaign = CreateCampaign();
        campaign.Schedule(Guid.NewGuid(), "Subject", "<p>Body</p>", null, "[]", DateTime.UtcNow);

        var second = campaign.Schedule(Guid.NewGuid(), "Subject", "<p>Body</p>", null, "[]", DateTime.UtcNow);

        Assert.True(second.IsFailure);
        Assert.Equal("Campaign.State", second.Error.Code);
    }

    [Fact]
    public void Campaign_completes_when_all_recipients_processed()
    {
        var campaign = CreateCampaign();
        campaign.Schedule(Guid.NewGuid(), "Subject", "<p>Body</p>", null, "[]", DateTime.UtcNow);
        campaign.MarkRunning();

        campaign.IncrementSent();
        campaign.IncrementFailed();

        Assert.Equal(CampaignStatus.Completed, campaign.Status);
        Assert.Equal(1, campaign.SentCount);
        Assert.Equal(1, campaign.FailedCount);
        Assert.NotNull(campaign.FinishedAtUtc);
    }

    private static EmailCampaign CreateCampaign() =>
        EmailCampaign
            .Create(
                Guid.NewGuid(),
                "Newsletter",
                CampaignType.Newsletter,
                Guid.NewGuid(),
                [("a@example.com", "A", null), ("b@example.com", "B", null)],
                null
            )
            .Value;
}
