using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.ClientForms.Application.Actions;
using TaxVision.Tasks.ClientForms.Application.Actions.Consumers;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Participants.Commands;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Application.Requests.Commands;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Tests.Dependencies;
using static TaxVision.Tasks.Tests.ClientForms.FormActionTestDoubles;

namespace TaxVision.Tasks.Tests.ClientForms;

// Varias requests al mismo email comparten un FormParticipant; cuando una crea o vincula su cliente,
// las demás siguen funcionando y heredan ese vínculo. Un segundo cliente distinto nunca se pierde en silencio.
public sealed class SharedFormParticipantTests
{
    private static readonly Guid ClientX = Guid.Parse("30000000-0000-0000-0000-0000000000f1");
    private static readonly Guid ClientY = Guid.Parse("30000000-0000-0000-0000-0000000000f2");

    [Fact]
    public void Verifying_again_a_participant_that_is_already_a_client_keeps_it_linked()
    {
        var participant = Participant();
        Assert.True(participant.LinkToCustomer(ClientX, Responsible, Now).IsSuccess);

        var verified = participant.MarkVerified(Now.AddMinutes(1));

        Assert.True(verified.IsSuccess);
        Assert.Equal(FormParticipantStatus.Linked, participant.Status);
        Assert.Equal(ClientX, participant.LinkedCustomerId);
    }

    [Fact]
    public async Task Creating_the_client_from_one_request_links_the_participants_other_open_requests()
    {
        var participant = Participant();
        var first = SubmittedWithCreation(participant, out var command);
        var second = ExternalRequest(Version(), participant);
        var audit = new ExtAuditLog();

        await CompletedAsync(first, command, ClientX, new ExtRequests(first, second), participant, audit);

        Assert.Equal(ClientX, participant.LinkedCustomerId);
        Assert.Equal(CustomerLinkStatus.Linked, first.CustomerLinkStatus);
        Assert.Equal(CustomerLinkStatus.Linked, second.CustomerLinkStatus);
        Assert.Equal(ClientX, second.CustomerId);
        Assert.Contains(
            audit.Entries,
            entry =>
                entry.Action == FormCustomerLinker.LinkInheritedAction
                && entry.Outcome == "success"
                && entry.RequestId == second.Id
        );
    }

    // Reproduce el hallazgo: antes, MarkVerified fallaba con el participante Linked y la verificación
    // respondía "invalid code" después de consumir el código.
    [Fact]
    public async Task Another_request_of_a_participant_that_became_a_client_can_still_be_verified()
    {
        var participant = Participant();
        Assert.True(participant.LinkToCustomer(ClientX, Responsible, Now).IsSuccess);
        var request = ExternalRequest(Version(), participant);
        var fixture = DirectedFixture(participant, request);
        await IssueChallengeAsync(fixture);

        var result = await VerifyAsync(fixture, "123456");

        Assert.True(result.IsSuccess);
        Assert.Equal(FormParticipantStatus.Linked, participant.Status);
        var session = Assert.Single(fixture.Sessions.Items);
        Assert.Equal(request.Id, session.FormRequestId);
        Assert.NotNull(Assert.Single(fixture.Challenges.Items).ConsumedAtUtc);
    }

    [Fact]
    public async Task A_wrong_code_on_such_a_request_is_still_the_only_reason_to_fail()
    {
        var participant = Participant();
        Assert.True(participant.LinkToCustomer(ClientX, Responsible, Now).IsSuccess);
        var fixture = DirectedFixture(participant, ExternalRequest(Version(), participant));
        await IssueChallengeAsync(fixture);

        var wrong = await VerifyAsync(fixture, "000000");
        var right = await VerifyAsync(fixture, "123456");

        Assert.Equal(PublicDirectedFormAccessErrors.VerificationFailed, wrong.Error);
        Assert.True(right.IsSuccess);
    }

    [Fact]
    public async Task A_second_client_for_a_participant_already_linked_keeps_the_original_and_warns()
    {
        var participant = Participant();
        Assert.True(participant.LinkToCustomer(ClientY, Responsible, Now).IsSuccess);
        var request = SubmittedWithCreation(participant, out var command);
        var audit = new ExtAuditLog();

        await CompletedAsync(request, command, ClientX, new ExtRequests(request), participant, audit);

        Assert.Equal(ClientY, participant.LinkedCustomerId);
        // La request sí queda con el cliente que creó: crear y vincular son pasos con resultados propios.
        Assert.Equal(ClientX, request.CustomerId);
        var warning = Assert.Single(audit.Entries, entry => entry.Action == FormCustomerLinker.LinkConflictAction);
        Assert.Equal("warning", warning.Outcome);
        Assert.Equal(request.Id, warning.RequestId);
        Assert.Contains(ClientY.ToString(), warning.Details, StringComparison.Ordinal);
        Assert.Contains(ClientX.ToString(), warning.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_result_for_a_request_already_linked_to_another_client_keeps_its_link_and_warns()
    {
        var participant = Participant();
        var request = SubmittedWithCreation(participant, out var command);
        Assert.True(request.TryAttachCustomer(ClientY, false, null, Now).IsSuccess);
        Assert.True(participant.LinkToCustomer(ClientY, Responsible, Now).IsSuccess);
        var audit = new ExtAuditLog();

        await CompletedAsync(request, command, ClientX, new ExtRequests(request), participant, audit);

        Assert.Equal(ClientY, request.CustomerId);
        Assert.Equal(FormActionRunStatus.Completed, request.ActionRuns.Single().Status);
        Assert.Equal(2, audit.Entries.Count(entry => entry.Action == FormCustomerLinker.LinkConflictAction));
    }

    [Fact]
    public async Task Manual_link_with_the_participant_already_a_client_links_the_request_and_warns()
    {
        var participant = Participant();
        Assert.True(participant.LinkToCustomer(ClientY, Responsible, Now).IsSuccess);
        var request = ExternalRequest(Version(), participant);
        Submit(request, participant);
        var audit = new ExtAuditLog();

        var result = await LinkFormRequestCustomerHandler.Handle(
            new LinkFormRequestCustomerCommand(
                TenantId,
                Responsible,
                true,
                request.Id,
                ClientX,
                request.RowVersion,
                Now
            ),
            new ExtRequests(request),
            new ExtVisibility(true),
            new FixedCustomerReader(),
            new ExtParticipants(participant),
            audit,
            new RecordingUnitOfWork(),
            new FakeMessageBus(),
            new TestCorrelationContext(),
            default
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(ClientX, request.CustomerId);
        Assert.Equal(ClientY, participant.LinkedCustomerId);
        Assert.Contains(
            audit.Entries,
            entry => entry.Action == FormCustomerLinker.LinkConflictAction && entry.Outcome == "warning"
        );
    }

    [Fact]
    public async Task A_sibling_that_collides_with_an_open_request_of_the_client_is_blocked_and_announced()
    {
        var participant = Participant();
        var first = SubmittedWithCreation(participant, out var command);
        var siblingVersion = Version();
        var sibling = ExternalRequest(siblingVersion, participant);
        var clientRequest = FormRequest
            .Create(
                TenantId,
                FormParticipantRef.Customer(ClientX),
                siblingVersion,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                FormRequestOrigin.Directed,
                null,
                null,
                Responsible,
                Now
            )
            .Value;
        var bus = new FakeMessageBus();

        await CompletedAsync(
            first,
            command,
            ClientX,
            new ExtRequests(first, sibling, clientRequest),
            participant,
            new ExtAuditLog(),
            bus
        );

        Assert.Equal(CustomerLinkStatus.Blocked, sibling.CustomerLinkStatus);
        Assert.Equal(clientRequest.Id, sibling.LinkConflictingRequestId);
        Assert.Contains(
            bus.Published,
            message =>
                message is FormActionFailedIntegrationEvent failed
                && failed.FormRequestId == sibling.Id
                && failed.ErrorCode == FormActionNames.LinkBlockedCode
        );
    }

    private static FormRequest SubmittedWithCreation(FormParticipant participant, out Guid command)
    {
        var request = ExternalRequest(Version(), participant);
        Submit(request, participant);
        command = Guid.NewGuid();
        Assert.True(request.RequestAction("create_client", FormActionNames.CreateCustomer, command, Now).IsSuccess);
        return request;
    }

    private static Task CompletedAsync(
        FormRequest request,
        Guid command,
        Guid customerId,
        ExtRequests requests,
        FormParticipant participant,
        ExtAuditLog audit,
        FakeMessageBus? bus = null
    ) =>
        CustomerCreationFromFormCompletedConsumer.Handle(
            new CustomerCreationFromFormCompletedIntegrationEvent
            {
                TenantId = TenantId,
                RequestId = command,
                SourceReferenceId = request.Id,
                CustomerId = customerId,
                Replayed = false,
            },
            requests,
            new ExtParticipants(participant),
            audit,
            new RecordingUnitOfWork(),
            bus ?? new FakeMessageBus(),
            new TestCorrelationContext(),
            NullLogger<CustomerCreationFromFormCompletedIntegrationEvent>.Instance,
            new RecordingClientFormsMetrics(),
            default
        );

    private static DirectedState DirectedFixture(FormParticipant participant, FormRequest request)
    {
        var notice = FormTemplateHandlerTestDoubles.IdentityNotice(TenantId, Responsible, Now);
        var grant = FormAccessGrant
            .Create(
                TenantId,
                request.Id,
                notice.Id,
                notice.Versions.Single().VersionNumber,
                "access-hash",
                "oken",
                Now.AddDays(14),
                Now
            )
            .Value;
        return new DirectedState(
            new ExtGrants(grant),
            new ExtChallenges(),
            new ExtSessions(),
            new ExtRequests(request),
            new ExtParticipants(participant),
            new MemoryConsentNoticeRepository(notice),
            new MemoryFormConsentRecordRepository()
        );
    }

    private static async Task IssueChallengeAsync(DirectedState state)
    {
        var issued = await RequestPublicDirectedFormChallengeHandler.Handle(
            new RequestPublicDirectedFormChallengeCommand("access-token", "office.test", Now),
            state.Grants,
            state.Challenges,
            state.Requests,
            state.Participants,
            state.Notices,
            new ExtSecurity(),
            new ExtHost(),
            new ExtCapabilities(),
            Options.Create(new ClientFormsOptions { External = new ClientFormsExternalOptions() }),
            new ExtCodec(),
            new RecordingUnitOfWork(),
            new FakeMessageBus(),
            new NoOpCorrelationContext(),
            new RecordingClientFormsMetrics(),
            default
        );
        Assert.True(issued.IsSuccess);
        Assert.Single(state.Challenges.Items);
    }

    private static Task<Result<PublicFormSessionResponse>> VerifyAsync(DirectedState state, string code) =>
        VerifyPublicDirectedFormChallengeHandler.Handle(
            new VerifyPublicDirectedFormChallengeCommand(
                "access-token",
                "office.test",
                code,
                "127.0.0.1",
                "tests",
                Now.AddMinutes(1)
            ),
            state.Grants,
            state.Challenges,
            state.Sessions,
            state.Requests,
            state.Participants,
            state.Notices,
            state.ConsentRecords,
            new ExtSecurity(),
            new ExtHost(),
            new ExtCapabilities(),
            new ExtAuditLog(),
            new RecordingUnitOfWork(),
            new RecordingClientFormsMetrics(),
            default
        );

    private sealed record DirectedState(
        ExtGrants Grants,
        ExtChallenges Challenges,
        ExtSessions Sessions,
        ExtRequests Requests,
        ExtParticipants Participants,
        MemoryConsentNoticeRepository Notices,
        MemoryFormConsentRecordRepository ConsentRecords
    );
}
