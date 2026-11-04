using TaxVision.Tasks.ClientForms.Application.Offboarding;

namespace TaxVision.Tasks.Tests.ClientForms;

// Pre-flight del retiro en Client Forms: enlaces no revocados y requests abiertas del empleado.
public sealed class ClientFormsOffboardingImpactHandlerTests
{
    private static readonly Guid TenantId = FormRequestFixtures.TenantId;

    [Fact]
    public async Task Counts_non_revoked_links_and_open_requests_of_the_employee()
    {
        var userId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var revoked = OffboardingFixtures.Link(TenantId, versionId, userId);
        revoked.Revoke();
        var links = new OffboardingIntakeLinks(
            OffboardingFixtures.Link(TenantId, versionId, userId),
            revoked,
            OffboardingFixtures.Link(TenantId, versionId, Guid.NewGuid())
        );
        var closed = FormRequestFixtures.CustomerRequest(requestedBy: userId);
        closed.Cancel(null, userId, FormRequestFixtures.Now);
        var requests = new OffboardingFormRequests(
            FormRequestFixtures.CustomerRequest(requestedBy: userId),
            closed,
            FormRequestFixtures.CustomerRequest()
        );

        var result = await ClientFormsOffboardingImpactHandler.Handle(
            new ClientFormsOffboardingImpactQuery(TenantId, userId),
            links,
            requests,
            CancellationToken.None
        );

        Assert.Equal(1, result.IntakeLinks);
        Assert.Equal(1, result.OpenFormRequests);
    }
}
