using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Application.Ingest;

/// <summary>Un correo rechazado se acusa, pero queda guardado para reprocesarlo.</summary>
public sealed class IngestRejectionRecorder(
    IUnmatchedIncomingEmailRepository unmatched,
    IUnitOfWork unitOfWork,
    IIngestMetrics metrics,
    ILogger<IngestRejectionRecorder> logger
)
{
    /// <summary>Remitente inválido: se guarda y cuenta en la métrica con su propio código, aparte de los del dominio.</summary>
    public async Task RejectInvalidSenderAsync(
        ConnectorsRawMessageReceivedIntegrationEvent evt,
        NormalizedIncomingMessage message,
        string rejectionCode,
        CancellationToken ct
    )
    {
        metrics.IngestRejected(nameof(UnmatchedReason.InvalidSender));
        await RecordAsync(evt, message, UnmatchedReason.InvalidSender, rejectionCode, ct);
    }

    private async Task RecordAsync(
        ConnectorsRawMessageReceivedIntegrationEvent evt,
        NormalizedIncomingMessage message,
        UnmatchedReason reason,
        string rejectionCode,
        CancellationToken ct
    )
    {
        var result = UnmatchedIncomingEmail.CreateRejected(
            evt.TenantId,
            evt.From,
            message.Subject,
            evt.ProviderMessageId,
            evt.ReceivedAtUtc,
            reason,
            rejectionCode,
            evt.AccountId,
            evt.ProviderCode,
            evt.InternetMessageId
        );
        if (result.IsFailure)
        {
            logger.LogError(
                "Could not record rejected event {EventId} ({Reason}, {RejectionCode}): {Error}",
                evt.EventId,
                reason,
                rejectionCode,
                result.Error.Code
            );
            return;
        }

        await unmatched.AddAsync(result.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task RejectIngestAsync(
        ConnectorsRawMessageReceivedIntegrationEvent evt,
        NormalizedIncomingMessage message,
        Error error,
        CancellationToken ct
    )
    {
        logger.LogError(
            "Ingest rejected event {EventId} for tenant {TenantId}: {RejectionCode}; recorded as IngestRejected.",
            evt.EventId,
            evt.TenantId,
            error.Code
        );
        metrics.IngestRejected(error.Code);
        await RecordAsync(evt, message, UnmatchedReason.IngestRejected, error.Code, ct);
    }
}
