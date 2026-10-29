using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using Wolverine;
using Kinds = BuildingBlocks.Messaging.ClientFormsIntegrationEvents.FormParticipantNotificationKinds;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Consumers;

/// <summary>
/// P-14.7: un externo sin vincular no recibe avisos desde el hecho (no hay cliente al que avisar), así
/// que Tasks los pide a Notification con un comando cifrado. Escucha sus propios hechos para no
/// tocar cada transición: el comando sale aunque el hecho lo haya publicado un job o un consumer.
/// </summary>
public static class ExternalParticipantNoticeConsumer
{
    public static Task Handle(
        FormRequestReturnedIntegrationEvent evt,
        IFormParticipantRepository participants,
        IProtectedPayloadCodec codec,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        RequestAsync(
            new(evt, evt.Participant, evt.FormRequestId, evt.Title, Kinds.Returned, CommentCount(evt)),
            new(participants, codec, bus, correlation),
            ct
        );

    public static Task Handle(
        FormRequestReminderDueIntegrationEvent evt,
        IFormParticipantRepository participants,
        IProtectedPayloadCodec codec,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        RequestAsync(
            new(evt, evt.Participant, evt.FormRequestId, evt.Title, Kinds.Reminder, Reminder(evt)),
            new(participants, codec, bus, correlation),
            ct
        );

    public static Task Handle(
        FormRequestCompletedIntegrationEvent evt,
        IFormParticipantRepository participants,
        IProtectedPayloadCodec codec,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        RequestAsync(
            new(evt, evt.Participant, evt.FormRequestId, evt.Title, Kinds.Completed, NoParams),
            new(participants, codec, bus, correlation),
            ct
        );

    // Mismo criterio que con el cliente: si nunca abrió la solicitud, cancelarla no se le avisa.
    public static Task Handle(
        FormRequestCancelledIntegrationEvent evt,
        IFormParticipantRepository participants,
        IProtectedPayloadCodec codec,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        !evt.NotifyClient
            ? Task.CompletedTask
            : RequestAsync(
                new(evt, evt.Participant, evt.FormRequestId, evt.Title, Kinds.Cancelled, NoParams),
                new(participants, codec, bus, correlation),
                ct
            );

    public static Task Handle(
        FormAttachmentRejectedIntegrationEvent evt,
        IFormParticipantRepository participants,
        IProtectedPayloadCodec codec,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        RequestAsync(
            new(evt, evt.Participant, evt.FormRequestId, evt.Title, Kinds.AttachmentRejected, Rejection(evt)),
            new(participants, codec, bus, correlation),
            ct
        );

    private static readonly IReadOnlyDictionary<string, string> NoParams = new Dictionary<string, string>();

    private static Dictionary<string, string> CommentCount(FormRequestReturnedIntegrationEvent evt) =>
        new() { ["comment_count"] = evt.FieldCommentCount.ToString() };

    private static Dictionary<string, string> Rejection(FormAttachmentRejectedIntegrationEvent evt) =>
        new() { ["file_extension"] = evt.FileExtension, ["reason_code"] = evt.ReasonCode };

    private static Dictionary<string, string> Reminder(FormRequestReminderDueIntegrationEvent evt) =>
        new() { ["is_overdue"] = evt.IsOverdue.ToString() };

    private static async Task RequestAsync(Notice notice, Senders senders, CancellationToken ct)
    {
        // Con cliente (directo o vinculado) el aviso ya sale del hecho en Notification.
        if (notice.Participant is not { Kind: "External", CustomerId: null, ParticipantId: { } participantId })
            return;

        var fact = notice.Fact;
        using (
            senders.Correlation.Push(
                string.IsNullOrWhiteSpace(fact.CorrelationId) ? fact.EventId.ToString("N") : fact.CorrelationId
            )
        )
        {
            var external = await senders.Participants.GetAsync(fact.TenantId, participantId, ct);
            if (external is not null)
                await senders.Bus.PublishAsync(Command(notice, external, senders));
        }
    }

    // El id del hecho fija el del comando: una reentrega pide el mismo aviso y Notification lo deduplica.
    private static FormParticipantNotificationRequestedIntegrationEvent Command(
        Notice notice,
        FormParticipant external,
        Senders senders
    ) =>
        new()
        {
            EventId = notice.Fact.EventId,
            CommandId = notice.Fact.EventId,
            TenantId = notice.Fact.TenantId,
            CorrelationId = senders.Correlation.CorrelationId,
            FormRequestId = notice.FormRequestId,
            Participant = notice.Participant,
            Kind = notice.Kind,
            Payload = senders.Codec.Protect(
                ClientFormProtectedPayloadSchemas.ParticipantNotification,
                new ProtectedPayloadBinding(notice.Fact.TenantId, notice.FormRequestId, notice.Participant),
                new FormParticipantNotificationPayload(
                    external.Email,
                    external.FullName,
                    notice.Title,
                    notice.Params,
                    external.Language
                )
            ),
        };

    private sealed record Notice(
        IntegrationEvent Fact,
        ClientFormParticipant Participant,
        Guid FormRequestId,
        string Title,
        string Kind,
        IReadOnlyDictionary<string, string> Params
    );

    private sealed record Senders(
        IFormParticipantRepository Participants,
        IProtectedPayloadCodec Codec,
        IMessageBus Bus,
        ICorrelationContext Correlation
    );
}
