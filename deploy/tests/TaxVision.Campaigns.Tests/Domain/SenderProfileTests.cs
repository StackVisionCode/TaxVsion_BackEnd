using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Senders;

namespace TaxVision.Campaigns.Tests.Domain;

public sealed class SenderProfileTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void Create_rejects_None_channel()
    {
        var result = SenderProfile.Create(Tenant, CampaignChannel.None, "n", "ref");
        Assert.True(result.IsFailure);
        Assert.Equal(SenderProfileErrors.ChannelInvalid, result.Error);
    }

    [Fact]
    public void Create_rejects_combined_channel_flags()
    {
        var combined = CampaignChannel.Email | CampaignChannel.Sms;
        var result = SenderProfile.Create(Tenant, combined, "n", "ref");
        Assert.True(result.IsFailure);
        Assert.Equal(SenderProfileErrors.ChannelInvalid, result.Error);
    }

    [Fact]
    public void Create_single_channel_starts_active()
    {
        var sender = SenderProfile.Create(Tenant, CampaignChannel.Email, "TaxVision", "no-reply@x.com").Value;
        Assert.True(sender.IsActive);
        Assert.Equal(CampaignChannel.Email, sender.Channel);
        Assert.Equal("no-reply@x.com", sender.SenderRef);
    }

    [Fact]
    public void Create_requires_name_and_senderRef()
    {
        Assert.True(SenderProfile.Create(Tenant, CampaignChannel.Email, " ", "ref").IsFailure);
        Assert.True(SenderProfile.Create(Tenant, CampaignChannel.Email, "n", " ").IsFailure);
    }

    [Fact]
    public void Disable_then_activate_toggles_status()
    {
        var sender = SenderProfile.Create(Tenant, CampaignChannel.Sms, "n", "InfoSMS").Value;
        sender.Disable();
        Assert.False(sender.IsActive);
        sender.Activate();
        Assert.True(sender.IsActive);
    }
}

public sealed class CampaignSenderSelectionTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();

    private static Campaign DraftEmailSms() =>
        Campaign.Create(Tenant, User, "C", CampaignChannel.Email | CampaignChannel.Sms, "body", "subj").Value;

    [Fact]
    public void SetSender_upserts_one_per_channel()
    {
        var campaign = DraftEmailSms();
        var s1 = Guid.NewGuid();
        var s2 = Guid.NewGuid();

        Assert.True(campaign.SetSender(CampaignChannel.Email, s1).IsSuccess);
        Assert.True(campaign.SetSender(CampaignChannel.Email, s2).IsSuccess); // upsert same channel

        Assert.Single(campaign.Senders);
        Assert.Equal(s2, campaign.Senders.Single().SenderProfileId);
    }

    [Fact]
    public void SetSender_rejects_channel_not_in_campaign()
    {
        var emailOnly = Campaign.Create(Tenant, User, "C", CampaignChannel.Email, "body", "subj").Value;
        var result = emailOnly.SetSender(CampaignChannel.Sms, Guid.NewGuid());
        Assert.True(result.IsFailure);
        Assert.Equal(CampaignErrors.SenderChannelNotSelected, result.Error);
    }

    [Fact]
    public void SetSender_rejects_combined_channel()
    {
        var campaign = DraftEmailSms();
        var result = campaign.SetSender(CampaignChannel.Email | CampaignChannel.Sms, Guid.NewGuid());
        Assert.True(result.IsFailure);
    }
}
