using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Requests.Commands.Cancel;
using Wolverine;

namespace TaxVision.Signature.Application.Requests.Commands.FromForm;

/// <summary>
/// P-18 — Tasks pide cancelar una firma pendiente (revisión nueva supera a la anterior), por la cola
/// signature-client-form-commands. Reutiliza el handler de cancelación existente; idempotente: si la
/// solicitud ya está en estado terminal, el cancel falla sin efecto y se ignora.
/// </summary>
public static class SignatureRequestCancellationRequestedConsumer
{
    public static async Task Handle(
        SignatureRequestCancellationRequestedIntegrationEvent command,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<SignatureRequestCancellationRequestedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(ResolveCorrelationId(command)))
        {
            var result = await bus.InvokeAsync<Result>(
                new CancelSignatureRequestCommand(
                    command.TenantId,
                    command.SignatureRequestId,
                    command.RequestedByUserId,
                    CanceledByUserDisplayName: null,
                    Reason: command.Reason
                ),
                ct
            );

            if (result.IsFailure)
                logger.LogInformation(
                    "Signature cancellation for {SignatureRequestId} had no effect: {Code}.",
                    command.SignatureRequestId,
                    result.Error.Code
                );
        }
    }

    private static string ResolveCorrelationId(SignatureRequestCancellationRequestedIntegrationEvent command) =>
        string.IsNullOrWhiteSpace(command.CorrelationId)
            ? command.SignatureRequestId.ToString("N")
            : command.CorrelationId;
}
