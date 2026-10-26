using System.Security.Cryptography;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using Kinds = BuildingBlocks.Messaging.ClientFormsIntegrationEvents.FormParticipantNotificationKinds;

namespace TaxVision.Notification.Application.Consumers.ClientForms;

/// <summary>
/// Avisos por email a un participante externo sin vincular (P-14.7). Va al email del comando, sin
/// <c>ContactPolicy</c> porque no hay cliente, igual que los firmantes externos de Signature. No lleva
/// enlace: el token se guarda hasheado y el enlace del correo de acceso sigue sirviendo.
/// </summary>
public static class FormParticipantNotificationRequestedConsumer
{
    public static async Task Handle(
        FormParticipantNotificationRequestedIntegrationEvent command,
        IProtectedPayloadCodec codec,
        ClientFormNotificationSender sender,
        ICorrelationContext correlation,
        ILogger<FormParticipantNotificationRequestedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(command.CorrelationId, command.CommandId)))
        {
            if (TemplateKey(command.Kind) is not { } templateKey)
            {
                logger.LogWarning(
                    "Client form participant notification {CommandId} has unsupported kind {Kind}; discarded.",
                    command.CommandId,
                    command.Kind
                );
                return;
            }

            var content = Unprotect(command, codec, logger);
            if (content is null)
                return;

            var result = await sender.SendExternalEmailAsync(
                new ClientFormExternalEmailRequest(
                    command.TenantId,
                    command.CommandId,
                    CatalogKind(command.Kind),
                    templateKey,
                    content.Email,
                    content.Language,
                    Variables(content)
                ),
                ct
            );
            if (result is { IsSuccess: false })
                logger.LogWarning(
                    "Client form participant notification {Kind} failed for request {FormRequestId} and command {CommandId}.",
                    command.Kind,
                    command.FormRequestId,
                    command.CommandId
                );
        }
    }

    private static FormParticipantNotificationPayload? Unprotect(
        FormParticipantNotificationRequestedIntegrationEvent command,
        IProtectedPayloadCodec codec,
        ILogger logger
    )
    {
        try
        {
            return codec.Unprotect<FormParticipantNotificationPayload>(
                command.Payload,
                ClientFormProtectedPayloadSchemas.ParticipantNotification,
                new ProtectedPayloadBinding(command.TenantId, command.FormRequestId, command.Participant)
            );
        }
        catch (CryptographicException)
        {
            // Reintentar no arregla un payload que no corresponde al sobre: se descarta.
            logger.LogWarning(
                "Security: discarded client form participant notification {CommandId} for tenant {TenantId}; protected payload validation failed.",
                command.CommandId,
                command.TenantId
            );
            return null;
        }
    }

    private static string? TemplateKey(string kind) =>
        kind switch
        {
            Kinds.Returned => "task.form_participant_returned.v1",
            Kinds.Reminder => "task.form_participant_reminder.v1",
            Kinds.Completed => "task.form_participant_completed.v1",
            Kinds.Cancelled => "task.form_participant_cancelled.v1",
            Kinds.AttachmentRejected => "task.form_participant_attachment_rejected.v1",
            _ => null,
        };

    private static string CatalogKind(string kind) =>
        kind switch
        {
            Kinds.Returned => NotificationKinds.TasksFormParticipantReturned,
            Kinds.Reminder => NotificationKinds.TasksFormParticipantReminder,
            Kinds.Completed => NotificationKinds.TasksFormParticipantCompleted,
            Kinds.Cancelled => NotificationKinds.TasksFormParticipantCancelled,
            Kinds.AttachmentRejected => NotificationKinds.TasksFormParticipantAttachmentRejected,
            _ => throw new InvalidOperationException($"Unsupported client form participant kind '{kind}'."),
        };

    private static Dictionary<string, object?> Variables(FormParticipantNotificationPayload content)
    {
        var variables = new Dictionary<string, object?>
        {
            ["participant_name"] = content.Name,
            ["form_title"] = content.Title,
            ["language"] = content.Language,
        };
        if (content.Params.TryGetValue("comment_count", out var comments) && int.TryParse(comments, out var count))
            variables["comment_count"] = count;
        if (content.Params.TryGetValue("file_extension", out var extension))
            variables["file_extension"] = extension;
        if (content.Params.TryGetValue("reason_code", out var reason))
        {
            variables["client_message"] = FormAttachmentRejectedConsumer.ClientMessage(reason);
            variables["client_message_es"] = FormAttachmentRejectedConsumer.SpanishClientMessage(reason);
        }
        if (content.Params.TryGetValue("is_overdue", out var overdue) && bool.TryParse(overdue, out var isOverdue))
            variables["is_overdue"] = isOverdue;
        return variables;
    }
}
