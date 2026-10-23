using System.Security.Cryptography;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Application.Consumers.ClientForms;

public static class FormAccessCodeDeliveryRequestedConsumer
{
    private const string TemplateKey = "task.form_access_code.v1";

    public static async Task Handle(
        FormAccessCodeDeliveryRequestedIntegrationEvent command,
        IProtectedPayloadCodec codec,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribe,
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

            var render = (
                await scribe.RenderAsync(
                    TemplateKey,
                    command.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["code"] = content.Code,
                        ["form_title"] = content.Title,
                        ["expires_at"] = command.ExpiresAtUtc.ToString("yyyy-MM-dd HH:mm"),
                    },
                    ct
                )
            ).EnsureRendered(TemplateKey);

            var dispatch = render.ToDispatchRequest(
                command.TenantId,
                content.Email,
                TemplateKey,
                command.CommandId,
                correlation.CorrelationId
            ) with
            {
                IdempotencyKey = command.CommandId.ToString("N"),
                Confidential = true,
            };
            var result = await gateway.QueueEmailAsync(dispatch, ct);
            if (!result.IsSuccess)
                logger.LogWarning(
                    "Client form access code delivery failed for challenge {ChallengeId} and command {CommandId}.",
                    command.ChallengeId,
                    command.CommandId
                );
        }
    }
}
