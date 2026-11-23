using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using TaxVision.Tasks.ClientForms.Application.Requests.Commands;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Tests.Dependencies;
using static TaxVision.Tasks.Tests.ClientForms.FormRequestFixtures;

namespace TaxVision.Tasks.Tests.ClientForms;

/// <summary>Devolver a un externo le manda un enlace nuevo en el mismo aviso de "Changes are needed".</summary>
public sealed class ReturnFormRequestExternalLinkTests
{
    [Fact]
    public async Task Returning_to_an_external_rotates_the_link_and_the_notice_carries_it()
    {
        var fixture = External();
        var bus = new FakeMessageBus();

        var result = await Return(fixture, bus);

        Assert.True(result.IsSuccess);
        Assert.Equal("session-hash", fixture.Grant.TokenHash);
        Assert.Equal(1, fixture.Grant.IssueCount);
        Assert.Single(bus.Published.OfType<FormRequestReturnedIntegrationEvent>());
        var notice = Assert.Single(bus.Published.OfType<FormParticipantNotificationRequestedIntegrationEvent>());
        Assert.Equal(FormParticipantNotificationKinds.Returned, notice.Kind);
        var payload = Unprotect(notice, fixture.Request.Id);
        Assert.Equal("session-token", payload.AccessToken);
        Assert.Equal(Now.AddDays(14), payload.AccessExpiresAtUtc);
        Assert.Equal("Guest Person", payload.Name);
        Assert.Equal("0", payload.Params["comment_count"]);
    }

    [Fact]
    public async Task The_system_rotation_does_not_use_up_the_resend_limit()
    {
        var fixture = External();
        for (var issue = 1; issue < FormAccessGrant.MaxIssues; issue++)
            Assert.True(fixture.Grant.Rotate($"hash-{issue}", "last", Now.AddDays(1), Now).IsSuccess);
        var bus = new FakeMessageBus();

        await Return(fixture, bus);

        Assert.Equal("session-hash", fixture.Grant.TokenHash);
        Assert.Equal(FormAccessGrant.MaxIssues, fixture.Grant.IssueCount);
        var notice = Assert.Single(bus.Published.OfType<FormParticipantNotificationRequestedIntegrationEvent>());
        Assert.Equal("session-token", Unprotect(notice, fixture.Request.Id).AccessToken);
    }

    [Fact]
    public async Task A_revoked_link_is_not_revived_and_the_notice_goes_without_it()
    {
        var fixture = External();
        fixture.Grant.Revoke(Now);
        var bus = new FakeMessageBus();

        await Return(fixture, bus);

        Assert.NotEqual("session-hash", fixture.Grant.TokenHash);
        var notice = Assert.Single(bus.Published.OfType<FormParticipantNotificationRequestedIntegrationEvent>());
        Assert.Null(Unprotect(notice, fixture.Request.Id).AccessToken);
    }

    [Fact]
    public async Task A_customer_gets_no_participant_notice_because_the_fact_notifies_them()
    {
        var version = Version();
        var request = CustomerRequest(version);
        Submit(request);
        var bus = new FakeMessageBus();

        var result = await ReturnFormRequestHandler.Handle(
            Command(request),
            new SingleFormRequestRepository(request),
            new FixedRequestVisibility(true),
            new MemoryFormTemplateVersionRepository(version),
            FormTemplateHandlerTestDoubles.Engine(),
            new PlainFormAnswersProtector(),
            new RecordingFormAuditLog(),
            new RecordingUnitOfWork(),
            bus,
            new TestCorrelationContext(),
            new RecordingClientFormsMetrics(),
            new ExtParticipants(),
            new ExtGrants(),
            new ExtSecurity(),
            FormActionTestDoubles.Codec,
            default
        );

        Assert.True(result.IsSuccess);
        Assert.IsType<FormRequestReturnedIntegrationEvent>(Assert.Single(bus.Published));
    }

    private sealed record Fixture(
        FormRequest Request,
        TaxVision.Tasks.ClientForms.Domain.Versions.FormTemplateVersion Version,
        FormParticipant Participant,
        FormAccessGrant Grant
    );

    private static Fixture External()
    {
        var participant = FormParticipant
            .Create(TenantId, "guest@example.com", "Guest Person", null, FormParticipantSource.DirectedRequest, Now)
            .Value;
        var version = Version();
        var request = FormRequest
            .Create(
                TenantId,
                FormParticipantRef.External(participant.Id),
                version,
                null,
                null,
                2026,
                null,
                Now.AddDays(7),
                null,
                null,
                FormRequestOrigin.Directed,
                null,
                UserId,
                UserId,
                Now
            )
            .Value;
        Assert.True(
            request
                .Submit(
                    new ProtectedAnswers("{\"schemaVersion\":1,\"values\":{}}", 1),
                    [],
                    [],
                    FormActor.ExternalSession(participant.Id, Guid.NewGuid()),
                    Now
                )
                .IsSuccess
        );
        var grant = FormAccessGrant
            .Create(TenantId, request.Id, Guid.NewGuid(), 1, "first-hash", "irst", Now.AddDays(14), Now)
            .Value;
        return new Fixture(request, version, participant, grant);
    }

    private static ReturnFormRequestCommand Command(FormRequest request) =>
        new(TenantId, UserId, true, request.Id, "Please review", [], request.RowVersion, Now);

    private static Task<BuildingBlocks.Results.Result<TaxVision.Tasks.ClientForms.Application.Requests.FormRequestResponse>> Return(
        Fixture fixture,
        FakeMessageBus bus
    ) =>
        ReturnFormRequestHandler.Handle(
            Command(fixture.Request),
            new SingleFormRequestRepository(fixture.Request),
            new FixedRequestVisibility(true),
            new MemoryFormTemplateVersionRepository(fixture.Version),
            FormTemplateHandlerTestDoubles.Engine(),
            new PlainFormAnswersProtector(),
            new RecordingFormAuditLog(),
            new RecordingUnitOfWork(),
            bus,
            new TestCorrelationContext(),
            new RecordingClientFormsMetrics(),
            new ExtParticipants(fixture.Participant),
            new ExtGrants(fixture.Grant),
            new ExtSecurity(),
            FormActionTestDoubles.Codec,
            default
        );

    private static FormParticipantNotificationPayload Unprotect(
        FormParticipantNotificationRequestedIntegrationEvent notice,
        Guid requestId
    ) =>
        FormActionTestDoubles.Codec.Unprotect<FormParticipantNotificationPayload>(
            notice.Payload,
            ClientFormProtectedPayloadSchemas.ParticipantNotification,
            new ProtectedPayloadBinding(TenantId, requestId, notice.Participant)
        );
}
