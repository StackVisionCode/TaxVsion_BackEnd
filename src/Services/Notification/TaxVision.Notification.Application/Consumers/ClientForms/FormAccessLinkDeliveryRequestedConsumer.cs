using System.Security.Cryptography;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers.ClientForms;

public static class FormAccessLinkDeliveryRequestedConsumer
{
    private const string TemplateKey = "task.form_access_link.v1";

    public static async Task Handle(
        FormAccessLinkDeliveryRequestedIntegrationEvent command,
        IProtectedPayloadCodec codec,
        ClientFormNotificationSender sender,
        ITenantHostResolver hosts,
        IOptions<PortalOptions> portal,
        ICorrelationContext correlation,
        ILogger<FormAccessLinkDeliveryRequestedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(command.CorrelationId, command.CommandId)))
        {
            var binding = new ProtectedPayloadBinding(command.TenantId, command.FormRequestId, command.Participant);
            FormAccessLinkDeliveryPayload content;
            try
            {
                content = codec.Unprotect<FormAccessLinkDeliveryPayload>(
                    command.Payload,
                    ClientFormProtectedPayloadSchemas.AccessLink,
                    binding
                );
            }
            catch (CryptographicException)
            {
                // Reintentar no arregla un payload que no corresponde al sobre: se descarta.
                logger.LogWarning(
                    "Security: discarded client form access link command {CommandId} for tenant {TenantId}; protected payload validation failed.",
                    command.CommandId,
                    command.TenantId
                );
                return;
            }

            var host = await hosts.ResolveHostAsync(command.TenantId, ct);
            var link = TenantEmailLinks.PublicFormAccess(host, portal.Value, content.AccessToken);
            var result = await sender.SendExternalEmailAsync(
                new ClientFormExternalEmailRequest(
                    command.TenantId,
                    command.CommandId,
                    NotificationKinds.TasksFormAccessLink,
                    TemplateKey,
                    content.Email,
                    content.Language,
                    new Dictionary<string, object?>
                    {
                        ["participant_name"] = content.Name,
                        ["form_title"] = content.Title,
                        ["access_link"] = link,
                        ["expires_at"] = command.ExpiresAtUtc.ToString("yyyy-MM-dd HH:mm"),
                    }
                ),
                ct
            );
            if (result is { IsSuccess: false })
                logger.LogWarning(
                    "Client form access link delivery failed for request {FormRequestId} and command {CommandId}.",
                    command.FormRequestId,
                    command.CommandId
                );
        }
    }
}
