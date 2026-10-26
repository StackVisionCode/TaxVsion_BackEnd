using System.Security.Cryptography;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers.ClientForms;

public static class FormAccessCodeDeliveryRequestedConsumer
{
    private const string TemplateKey = "task.form_access_code.v1";

    public static async Task Handle(
        FormAccessCodeDeliveryRequestedIntegrationEvent command,
        IProtectedPayloadCodec codec,
        ClientFormNotificationSender sender,
        ICorrelationContext correlation,
        ILogger<FormAccessCodeDeliveryRequestedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(command.CorrelationId, command.CommandId)))
        {
            var binding = new ProtectedPayloadBinding(
                command.TenantId,
                command.FormRequestId,
                command.Participant,
                command.IntakeLinkId,
                command.ChallengeId
            );
            FormAccessCodeDeliveryPayload content;
            try
            {
                content = codec.Unprotect<FormAccessCodeDeliveryPayload>(
                    command.Payload,
                    ClientFormProtectedPayloadSchemas.AccessCode,
                    binding
                );
            }
            catch (CryptographicException)
            {
                // Reintentar no arregla un payload que no corresponde al sobre: se descarta.
                logger.LogWarning(
                    "Security: discarded client form access code command {CommandId} for tenant {TenantId}; protected payload validation failed.",
                    command.CommandId,
                    command.TenantId
                );
                return;
            }

            var result = await sender.SendExternalEmailAsync(
                new ClientFormExternalEmailRequest(
                    command.TenantId,
                    command.CommandId,
                    NotificationKinds.TasksFormAccessCode,
                    TemplateKey,
                    content.Email,
                    content.Language,
                    new Dictionary<string, object?>
                    {
                        ["code"] = content.Code,
                        ["form_title"] = content.Title,
                        ["expires_at"] = command.ExpiresAtUtc.ToString("yyyy-MM-dd HH:mm"),
                    }
                ),
                ct
            );
            if (result is { IsSuccess: false })
                logger.LogWarning(
                    "Client form access code delivery failed for challenge {ChallengeId} and command {CommandId}.",
                    command.ChallengeId,
                    command.CommandId
                );
        }
    }
}
