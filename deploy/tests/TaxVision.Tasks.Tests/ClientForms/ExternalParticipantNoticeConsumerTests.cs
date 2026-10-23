using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using TaxVision.Tasks.ClientForms.Application.Requests.Consumers;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.Tests.Dependencies;
using Xunit;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class ExternalParticipantNoticeConsumerTests
{
    private static readonly Guid TenantId = FormActionTestDoubles.TenantId;

    [Fact]
    public async Task Returned_to_an_unlinked_external_requests_a_confidential_notice()
    {
        var participant = Participant();
        var bus = new FakeMessageBus();
        var evt = Returned(External(participant.Id));

        await ExternalParticipantNoticeConsumer.Handle(
            evt,
            new ActionParticipantRepository(participant),
            FormActionTestDoubles.Codec,
            bus,
            new TestCorrelationContext(),
            CancellationToken.None
        );

        var command = Assert.IsType<FormParticipantNotificationRequestedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(FormParticipantNotificationKinds.Returned, command.Kind);
        Assert.Equal(evt.EventId, command.CommandId);
        Assert.Equal(evt.FormRequestId, command.FormRequestId);
        Assert.DoesNotContain("guest@example.com", command.Payload.Cipher, StringComparison.OrdinalIgnoreCase);

        var payload = FormActionTestDoubles.Codec.Unprotect<FormParticipantNotificationPayload>(
            command.Payload,
            ClientFormProtectedPayloadSchemas.ParticipantNotification,
            new ProtectedPayloadBinding(TenantId, evt.FormRequestId, command.Participant)
        );
        Assert.Equal("guest@example.com", payload.Email);
        Assert.Equal("Guest Person", payload.Name);
        Assert.Equal("Intake", payload.Title);
        Assert.Equal("3", payload.Params["comment_count"]);
    }

    [Fact]
    public async Task Customer_and_linked_external_get_no_command_because_the_fact_already_notifies_them()
    {
        var participant = Participant();
        var bus = new FakeMessageBus();
        var repository = new ActionParticipantRepository(participant);

        await ExternalParticipantNoticeConsumer.Handle(
            Returned(new ClientFormParticipant { Kind = "Customer", CustomerId = Guid.NewGuid() }),
            repository,
            FormActionTestDoubles.Codec,
            bus,
            new TestCorrelationContext(),
            CancellationToken.None
        );
        await ExternalParticipantNoticeConsumer.Handle(
            Returned(
                new ClientFormParticipant
                {
                    Kind = "External",
                    ParticipantId = participant.Id,
                    CustomerId = Guid.NewGuid(),
                }
            ),
            repository,
            FormActionTestDoubles.Codec,
            bus,
            new TestCorrelationContext(),
            CancellationToken.None
        );

        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Cancelled_before_the_external_opened_it_is_not_announced()
    {
        var participant = Participant();
        var bus = new FakeMessageBus();

        await ExternalParticipantNoticeConsumer.Handle(
            new FormRequestCancelledIntegrationEvent
            {
                TenantId = TenantId,
                FormRequestId = Guid.NewGuid(),
                Participant = External(participant.Id),
                Title = "Intake",
                Reason = "staff_cancelled",
                CancelledByUserId = Guid.NewGuid(),
                NotifyClient = false,
            },
            new ActionParticipantRepository(participant),
            FormActionTestDoubles.Codec,
            bus,
            new TestCorrelationContext(),
            CancellationToken.None
        );

        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Attachment_rejection_carries_only_the_extension_and_the_reason_code()
    {
        var participant = Participant();
        var bus = new FakeMessageBus();
        var evt = new FormAttachmentRejectedIntegrationEvent
        {
            TenantId = TenantId,
            FormRequestId = Guid.NewGuid(),
            Participant = External(participant.Id),
            Title = "Intake",
            AttachmentId = Guid.NewGuid(),
            FileId = Guid.NewGuid(),
            FileExtension = "pdf",
            ReasonCode = "infected",
            RequestedByUserId = Guid.NewGuid(),
        };

        await ExternalParticipantNoticeConsumer.Handle(
            evt,
            new ActionParticipantRepository(participant),
            FormActionTestDoubles.Codec,
            bus,
            new TestCorrelationContext(),
            CancellationToken.None
        );

        var command = Assert.IsType<FormParticipantNotificationRequestedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(FormParticipantNotificationKinds.AttachmentRejected, command.Kind);
        var payload = FormActionTestDoubles.Codec.Unprotect<FormParticipantNotificationPayload>(
            command.Payload,
            ClientFormProtectedPayloadSchemas.ParticipantNotification,
            new ProtectedPayloadBinding(TenantId, evt.FormRequestId, command.Participant)
        );
        Assert.Equal(["file_extension", "reason_code"], payload.Params.Keys.Order());
        Assert.Equal("infected", payload.Params["reason_code"]);
    }

    [Fact]
    public async Task Unknown_participant_requests_nothing()
    {
        var bus = new FakeMessageBus();

        await ExternalParticipantNoticeConsumer.Handle(
            Returned(External(Guid.NewGuid())),
            new ActionParticipantRepository(Participant()),
            FormActionTestDoubles.Codec,
            bus,
            new TestCorrelationContext(),
            CancellationToken.None
        );

        Assert.Empty(bus.Published);
    }

    private static FormParticipant Participant() =>
        FormParticipant
            .Create(
                TenantId,
                "guest@example.com",
                "Guest Person",
                null,
                FormParticipantSource.DirectedRequest,
                FormActionTestDoubles.Now
            )
            .Value;

    private static ClientFormParticipant External(Guid participantId) =>
        new() { Kind = "External", ParticipantId = participantId };

    private static FormRequestReturnedIntegrationEvent Returned(ClientFormParticipant participant) =>
        new()
        {
            TenantId = TenantId,
            FormRequestId = Guid.NewGuid(),
            Participant = participant,
            Title = "Intake",
            RevisionNumber = 1,
            FieldCommentCount = 3,
            ReturnedByUserId = Guid.NewGuid(),
        };
}
