using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers.ClientForms;

public static class FormAttachmentRejectedConsumer
{
    private const string TemplateKey = "task.form_attachment_rejected.v1";

    public static async Task Handle(
        FormAttachmentRejectedIntegrationEvent evt,
        ClientFormNotificationSender sender,
        ICustomerAssignmentAudienceRepository assignments,
        IRecipientResolver recipients,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            await NotifyCustomerAsync(evt, sender, recipients, ct);
            await NotifyStaffAsync(evt, sender, assignments, recipients, ct);
        }
    }

    private static async Task NotifyCustomerAsync(
        FormAttachmentRejectedIntegrationEvent evt,
        ClientFormNotificationSender sender,
        IRecipientResolver recipients,
        CancellationToken ct
    )
    {
        if (evt.Participant.CustomerId is not { } customerId)
            return;
        var message = ClientMessage(evt.ReasonCode);
        await ClientFormCustomerNotifier.NotifyAsync(
            new ClientFormCustomerNotification(
                evt.TenantId,
                customerId,
                evt.FormRequestId,
                evt.EventId,
                evt.OccurredOn,
                NotificationKinds.TasksFormAttachmentRejected,
                TemplateKey,
                "A file needs your attention",
                message,
                new Dictionary<string, object?>
                {
                    ["request_title"] = evt.Title,
                    ["file_extension"] = evt.FileExtension,
                    ["client_message"] = message,
                    ["client_message_es"] = SpanishClientMessage(evt.ReasonCode),
                },
                new Dictionary<string, string> { ["title"] = evt.Title }
            ),
            sender,
            recipients,
            ct
        );
    }

    private static async Task NotifyStaffAsync(
        FormAttachmentRejectedIntegrationEvent evt,
        ClientFormNotificationSender sender,
        ICustomerAssignmentAudienceRepository assignments,
        IRecipientResolver resolver,
        CancellationToken ct
    )
    {
        var recipients = await ClientFormStaffRecipients.ResolveRequesterForReadAsync(
            evt.TenantId,
            evt.Participant.CustomerId,
            evt.RequestedByUserId,
            assignments,
            resolver,
            ct
        );
        foreach (var userId in recipients)
            await sender.SendAsync(
                new ClientFormNotificationRequest(
                    evt.TenantId,
                    userId,
                    evt.FormRequestId,
                    evt.EventId,
                    evt.OccurredOn,
                    NotificationKinds.TasksFormAttachmentRejectedStaff,
                    "task.form_attachment_rejected_staff.v1",
                    string.Empty,
                    "A client file was rejected",
                    evt.Title,
                    new Dictionary<string, object?>(),
                    new Dictionary<string, string> { ["title"] = evt.Title },
                    NotificationCategory.ClientRequests,
                    evt.Participant.CustomerId
                ),
                ct
            );
    }

    // También lo usa el aviso a externos: el mismo texto fijo por código, nunca texto libre.
    internal static string ClientMessage(string reasonCode) =>
        reasonCode switch
        {
            "infected" =>
                "The file couldn't be accepted because our security scan flagged it. Please upload a different copy.",
            "blocked_by_policy" or "scan_failed" =>
                "The file type or content isn't allowed. Please upload a PDF or an image.",
            "not_owned" or "wrong_folder" => "The file couldn't be linked to this form. Please upload it again.",
            "upload_incomplete" => "The upload didn't finish. Please try again.",
            _ => "The file couldn't be accepted. Please upload it again.",
        };

    // Mismo texto fijo en español; la plantilla elige por language.
    internal static string SpanishClientMessage(string reasonCode) =>
        reasonCode switch
        {
            "infected" =>
                "No pudimos aceptar el archivo porque nuestro análisis de seguridad lo marcó. Sube otra copia.",
            "blocked_by_policy" or "scan_failed" =>
                "El tipo o el contenido del archivo no está permitido. Sube un PDF o una imagen.",
            "not_owned" or "wrong_folder" => "No pudimos vincular el archivo a este formulario. Vuelve a subirlo.",
            "upload_incomplete" => "La carga no terminó. Inténtalo de nuevo.",
            _ => "No pudimos aceptar el archivo. Vuelve a subirlo.",
        };
}
