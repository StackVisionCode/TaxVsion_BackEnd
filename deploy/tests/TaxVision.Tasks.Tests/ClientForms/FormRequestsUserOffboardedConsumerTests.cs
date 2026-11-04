using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Offboarding;
using TaxVision.Tasks.ClientForms.Domain.Audit;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.Tests.ClientForms;

/// <summary>
/// Al retirar al responsable efectivo de requests abiertas: pasan al sucesor elegible, si no a quien ejecutó
/// el retiro; si nadie es elegible quedan como estaban y señaladas. RequestedByUserId nunca cambia.
/// </summary>
public sealed class FormRequestsUserOffboardedConsumerTests
{
    private static readonly Guid TenantId = FormRequestFixtures.TenantId;
    private static readonly Guid Leaver = Guid.NewGuid();
    private static readonly Guid Successor = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();

    [Fact]
    public async Task Open_requests_of_the_leaver_move_to_the_successor_keeping_their_provenance()
    {
        var responsible = WithResponsible(Leaver);
        var requestedOnly = FormRequestFixtures.CustomerRequest(requestedBy: Leaver);
        var someoneElses = WithResponsible(Guid.NewGuid());
        var fixture = new Fixture([Successor, Admin], responsible, requestedOnly, someoneElses);

        await fixture.RunAsync(OffboardingFixtures.Offboarded(TenantId, Leaver, Successor, Admin));

        Assert.Equal(Successor, responsible.ResponsibleUserId);
        Assert.Equal(Successor, requestedOnly.ResponsibleUserId);
        Assert.Equal(Leaver, requestedOnly.RequestedByUserId);
        Assert.NotEqual(Successor, someoneElses.ResponsibleUserId);
        Assert.Equal(2, fixture.Audit.Actions.Count(a => a == FormAuditAction.RequestResponsibleChanged));
    }

    [Fact]
    public async Task Without_an_eligible_successor_they_go_to_the_admin_who_offboarded()
    {
        var request = WithResponsible(Leaver);
        var fixture = new Fixture([Admin], request);

        await fixture.RunAsync(OffboardingFixtures.Offboarded(TenantId, Leaver, Successor, Admin));

        Assert.Equal(Admin, request.ResponsibleUserId);
    }

    [Fact]
    public async Task A_successor_without_the_review_permission_is_skipped()
    {
        var request = WithResponsible(Leaver);
        var fixture = new Fixture([Successor, Admin], [Successor], request);

        await fixture.RunAsync(OffboardingFixtures.Offboarded(TenantId, Leaver, Successor, Admin));

        Assert.Equal(Admin, request.ResponsibleUserId);
    }

    [Fact]
    public async Task When_nobody_is_eligible_nothing_moves_and_the_requests_are_flagged()
    {
        var request = WithResponsible(Leaver);
        var fixture = new Fixture([], request);

        await fixture.RunAsync(OffboardingFixtures.Offboarded(TenantId, Leaver, null, null));

        Assert.Equal(Leaver, request.ResponsibleUserId);
        Assert.Equal([FormAuditAction.RequestResponsibleUnavailable], fixture.Audit.Actions);
    }

    [Fact]
    public async Task Closed_requests_are_not_touched()
    {
        var cancelled = WithResponsible(Leaver);
        cancelled.Cancel("No longer needed", Admin, FormRequestFixtures.Now);
        var fixture = new Fixture([Successor], cancelled);

        await fixture.RunAsync(OffboardingFixtures.Offboarded(TenantId, Leaver, Successor));

        Assert.Equal(Leaver, cancelled.ResponsibleUserId);
        Assert.Empty(fixture.Audit.Actions);
    }

    [Fact]
    public async Task Redelivery_is_a_no_op()
    {
        var request = WithResponsible(Leaver);
        var fixture = new Fixture([Successor], request);
        var msg = OffboardingFixtures.Offboarded(TenantId, Leaver, Successor);

        await fixture.RunAsync(msg);
        await fixture.RunAsync(msg);

        Assert.Equal(Successor, request.ResponsibleUserId);
        Assert.Single(fixture.Audit.Actions);
    }

    private static FormRequest WithResponsible(Guid responsible)
    {
        var request = FormRequestFixtures.CustomerRequest();
        request.AssignResponsible(responsible, FormRequestFixtures.Now);
        return request;
    }

    private sealed class Fixture(Guid[] active, Guid[] withoutPermissions, params FormRequest[] requests)
    {
        public Fixture(Guid[] active, params FormRequest[] requests)
            : this(active, [], requests) { }

        public RecordingFormAuditLog Audit { get; } = new();

        public Task RunAsync(UserOffboardedIntegrationEvent msg) =>
            FormRequestsUserOffboardedConsumer.Handle(
                msg,
                new OffboardingFormRequests(requests),
                new OffboardingResponsibleUsers(active, withoutPermissions),
                Audit,
                new RecordingUnitOfWork(),
                new NoOpCorrelationContext(),
                NullLogger<FormRequest>.Instance,
                CancellationToken.None
            );
    }
}
