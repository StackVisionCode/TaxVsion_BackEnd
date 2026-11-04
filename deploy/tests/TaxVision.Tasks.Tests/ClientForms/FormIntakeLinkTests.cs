using TaxVision.Tasks.ClientForms.Domain.Participants;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class FormIntakeLinkTests
{
    private static readonly Guid TenantId = FormRequestFixtures.TenantId;

    [Fact]
    public void Reassigning_the_responsible_keeps_the_status_and_the_creator()
    {
        var creator = Guid.NewGuid();
        var successor = Guid.NewGuid();
        var link = OffboardingFixtures.Link(TenantId, Guid.NewGuid(), creator);
        link.Pause();

        var result = link.ReassignResponsible(successor);

        Assert.True(result.IsSuccess);
        Assert.Equal(successor, link.ResponsibleUserId);
        Assert.Equal(creator, link.CreatedByUserId);
        Assert.Equal(FormIntakeLinkStatus.Paused, link.Status);
    }

    [Fact]
    public void A_revoked_link_cannot_change_its_responsible()
    {
        var owner = Guid.NewGuid();
        var link = OffboardingFixtures.Link(TenantId, Guid.NewGuid(), owner);
        link.Revoke();

        var result = link.ReassignResponsible(Guid.NewGuid());

        Assert.Equal(FormParticipantErrors.IntakeLinkUnavailable, result.Error);
        Assert.Equal(owner, link.ResponsibleUserId);
    }

    [Fact]
    public void The_responsible_cannot_be_empty()
    {
        var link = OffboardingFixtures.Link(TenantId, Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(FormParticipantErrors.IntakeLinkInvalid, link.ReassignResponsible(Guid.Empty).Error);
    }
}
