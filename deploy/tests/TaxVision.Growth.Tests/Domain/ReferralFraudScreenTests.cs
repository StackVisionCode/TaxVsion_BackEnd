using TaxVision.Referrals.Domain.Attributions;
using TaxVision.Referrals.Domain.Fraud;

namespace TaxVision.Growth.Tests.Domain;

/// <summary>
/// Una oficina no gana premio por referirse a sí misma: el mismo usuario se bloquea solo; el mismo email normalizado
/// o un email desechable abren revisión.
/// </summary>
public sealed class ReferralFraudScreenTests
{
    private static readonly Guid Owner = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherUser = Guid.Parse("40000000-0000-0000-0000-000000000002");
    private static readonly Guid NewOffice = Guid.Parse("40000000-0000-0000-0000-000000000003");

    [Fact]
    public void Self_referral_by_the_same_user_is_blocked()
    {
        var finding = ReferralFraudScreen.Evaluate(Linked(refereeUser: Owner), null, null);

        Assert.Equal(ReferralFraudVerdict.Block, finding.Verdict);
        Assert.Equal(ReferralFraudScreen.SameUser, finding.Signal);
    }

    [Fact]
    public void Self_referral_opens_a_review()
    {
        var finding = ReferralFraudScreen.Evaluate(Linked(OtherUser), Signals("v1:same"), Signals("v1:same"));

        Assert.Equal(ReferralFraudVerdict.Review, finding.Verdict);
        Assert.Equal(ReferralFraudScreen.SameOwnerEmail, finding.Signal);
    }

    [Fact]
    public void Disposable_referee_email_opens_a_review()
    {
        var finding = ReferralFraudScreen.Evaluate(
            Linked(OtherUser),
            new OfficeOwnerSignals("v1:referee", OwnerEmailDisposable: true),
            Signals("v1:referrer")
        );

        Assert.Equal(ReferralFraudVerdict.Review, finding.Verdict);
        Assert.Equal(ReferralFraudScreen.DisposableRefereeEmail, finding.Signal);
    }

    [Fact]
    public void The_same_user_wins_over_the_email_signals()
    {
        var finding = ReferralFraudScreen.Evaluate(Linked(Owner), Signals("v1:same"), Signals("v1:same"));

        Assert.Equal(ReferralFraudVerdict.Block, finding.Verdict);
    }

    [Fact]
    public void Different_owners_are_clear()
    {
        var finding = ReferralFraudScreen.Evaluate(Linked(OtherUser), Signals("v1:referee"), Signals("v1:referrer"));

        Assert.Equal(ReferralFraudVerdict.Clear, finding.Verdict);
        Assert.Null(finding.Signal);
    }

    [Fact]
    public void Without_the_owner_emails_only_the_user_is_compared()
    {
        Assert.Equal(ReferralFraudVerdict.Clear, ReferralFraudScreen.Evaluate(Linked(OtherUser), null, null).Verdict);
    }

    [Fact]
    public void An_attribution_not_linked_to_its_office_is_not_screened()
    {
        var program = GrowthTestData.CreateActiveTenantReferralProgram();
        var attribution = GrowthTestData.CreateActiveAttribution(
            program,
            GrowthTestData.CreateReferralCode(program, ownerUserId: Owner)
        );

        Assert.Equal(ReferralFraudVerdict.Clear, ReferralFraudScreen.Evaluate(attribution, null, null).Verdict);
    }

    [Fact]
    public void Linking_keeps_the_user_who_completed_the_signup()
    {
        var attribution = Linked(OtherUser);

        Assert.Equal(OtherUser, attribution.RefereeUserId);
        Assert.True(attribution.LinkRefereeTenant(NewOffice, GrowthTestData.NowUtc, Owner).IsSuccess);
        Assert.Equal(OtherUser, attribution.RefereeUserId);
    }

    private static OfficeOwnerSignals Signals(string subject) => new(subject, OwnerEmailDisposable: false);

    private static ReferralAttribution Linked(Guid refereeUser)
    {
        var program = GrowthTestData.CreateActiveTenantReferralProgram();
        var attribution = GrowthTestData.CreateActiveAttribution(
            program,
            GrowthTestData.CreateReferralCode(program, ownerUserId: Owner)
        );
        Assert.True(attribution.LinkRefereeTenant(NewOffice, GrowthTestData.NowUtc, refereeUser).IsSuccess);
        return attribution;
    }
}
