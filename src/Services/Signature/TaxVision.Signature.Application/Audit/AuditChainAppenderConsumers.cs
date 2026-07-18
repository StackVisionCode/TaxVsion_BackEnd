using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Audit;

namespace TaxVision.Signature.Application.Audit;

/// <summary>
/// Consumers Wolverine que appendean cada evento clave a la cadena HMAC. Cada consumer
/// mantiene UNA sola responsabilidad: mapear su evento a un <c>SignatureAuditEventKind</c>
/// + payload y delegar en <see cref="IAuditChainAppender"/>. Ninguno cambia estado del
/// aggregate ni publica más eventos — la cadena es un registro paralelo.
///
/// <para>
/// Al agrupar todos los consumers en un archivo se mantiene el bounded context claro
/// y evita 15 archivos vacíos. Cada método es independiente.
/// </para>
/// </summary>
public static class AuditChainAppenderConsumers
{
    public static async Task Handle(
        SignatureRequestCreatedIntegrationEvent evt,
        IAuditChainAppender appender,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SignatureAuditEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(evt.CorrelationId))
        {
            var payload = new
            {
                evt.SignatureRequestId,
                evt.CreatedByUserId,
                evt.Category,
                evt.OriginalFileId,
                evt.ExpiresAtUtc,
            };
            await AppendAsync(
                evt.TenantId,
                evt.SignatureRequestId,
                SignatureAuditEventKind.RequestCreated,
                evt.OccurredOn,
                payload,
                appender,
                unitOfWork,
                logger,
                ct
            );
        }
    }

    public static async Task Handle(
        SignatureRequestSentIntegrationEvent evt,
        IAuditChainAppender appender,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SignatureAuditEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(evt.CorrelationId))
        {
            var payload = new
            {
                evt.SignatureRequestId,
                evt.SentAtUtc,
                SignerCount = evt.SignerIds.Count,
            };
            await AppendAsync(
                evt.TenantId,
                evt.SignatureRequestId,
                SignatureAuditEventKind.RequestSent,
                evt.SentAtUtc,
                payload,
                appender,
                unitOfWork,
                logger,
                ct
            );
        }
    }

    public static async Task Handle(
        SignerConsentAcceptedIntegrationEvent evt,
        IAuditChainAppender appender,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SignatureAuditEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(evt.CorrelationId))
        {
            var payload = new
            {
                evt.SignerId,
                evt.AcceptedAtUtc,
                evt.ClientIp,
            };
            await AppendAsync(
                evt.TenantId,
                evt.SignatureRequestId,
                SignatureAuditEventKind.ConsentAccepted,
                evt.AcceptedAtUtc,
                payload,
                appender,
                unitOfWork,
                logger,
                ct
            );
        }
    }

    public static async Task Handle(
        SignerPinVerifiedIntegrationEvent evt,
        IAuditChainAppender appender,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SignatureAuditEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(evt.CorrelationId))
        {
            var payload = new
            {
                evt.SignerId,
                evt.VerifiedAtUtc,
                evt.ClientIp,
            };
            await AppendAsync(
                evt.TenantId,
                evt.SignatureRequestId,
                SignatureAuditEventKind.PinVerified,
                evt.VerifiedAtUtc,
                payload,
                appender,
                unitOfWork,
                logger,
                ct
            );
        }
    }

    public static async Task Handle(
        SignerPinFailedIntegrationEvent evt,
        IAuditChainAppender appender,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SignatureAuditEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(evt.CorrelationId))
        {
            var payload = new
            {
                evt.SignerId,
                evt.AttemptedAtUtc,
                evt.FailedAttempts,
                evt.LockedUntilUtc,
            };
            await AppendAsync(
                evt.TenantId,
                evt.SignatureRequestId,
                SignatureAuditEventKind.PinFailed,
                evt.AttemptedAtUtc,
                payload,
                appender,
                unitOfWork,
                logger,
                ct
            );
        }
    }

    public static async Task Handle(
        SignerVerificationSucceededIntegrationEvent evt,
        IAuditChainAppender appender,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SignatureAuditEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(evt.CorrelationId))
        {
            var payload = new
            {
                evt.SignerId,
                evt.Method,
                evt.VerifiedAtUtc,
                evt.ClientIp,
            };
            await AppendAsync(
                evt.TenantId,
                evt.SignatureRequestId,
                SignatureAuditEventKind.ChallengeVerified,
                evt.VerifiedAtUtc,
                payload,
                appender,
                unitOfWork,
                logger,
                ct
            );
        }
    }

    public static async Task Handle(
        SignerVerificationFailedIntegrationEvent evt,
        IAuditChainAppender appender,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SignatureAuditEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(evt.CorrelationId))
        {
            var payload = new
            {
                evt.SignerId,
                evt.Method,
                evt.AttemptedAtUtc,
                evt.FailedAttempts,
            };
            await AppendAsync(
                evt.TenantId,
                evt.SignatureRequestId,
                SignatureAuditEventKind.ChallengeFailed,
                evt.AttemptedAtUtc,
                payload,
                appender,
                unitOfWork,
                logger,
                ct
            );
        }
    }

    public static async Task Handle(
        DocumentSignedIntegrationEvent evt,
        IAuditChainAppender appender,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SignatureAuditEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(evt.CorrelationId))
        {
            var payload = new
            {
                evt.SignerId,
                evt.SignedAtUtc,
                evt.ClientIp,
                evt.IsRequestCompleted,
            };
            await AppendAsync(
                evt.TenantId,
                evt.SignatureRequestId,
                SignatureAuditEventKind.DocumentSigned,
                evt.SignedAtUtc,
                payload,
                appender,
                unitOfWork,
                logger,
                ct
            );
        }
    }

    public static async Task Handle(
        SignerRejectedIntegrationEvent evt,
        IAuditChainAppender appender,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SignatureAuditEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(evt.CorrelationId))
        {
            var payload = new
            {
                evt.SignerId,
                evt.RejectedAtUtc,
                evt.Reason,
            };
            await AppendAsync(
                evt.TenantId,
                evt.SignatureRequestId,
                SignatureAuditEventKind.SignerRejected,
                evt.RejectedAtUtc,
                payload,
                appender,
                unitOfWork,
                logger,
                ct
            );
        }
    }

    public static async Task Handle(
        SignatureRequestCanceledIntegrationEvent evt,
        IAuditChainAppender appender,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SignatureAuditEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(evt.CorrelationId))
        {
            var payload = new
            {
                evt.CanceledAtUtc,
                evt.CanceledByUserId,
                evt.Reason,
            };
            await AppendAsync(
                evt.TenantId,
                evt.SignatureRequestId,
                SignatureAuditEventKind.RequestCanceled,
                evt.CanceledAtUtc,
                payload,
                appender,
                unitOfWork,
                logger,
                ct
            );
        }
    }

    public static async Task Handle(
        SignatureRequestCompletedIntegrationEvent evt,
        IAuditChainAppender appender,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SignatureAuditEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(evt.CorrelationId))
        {
            var payload = new
            {
                evt.CompletedAtUtc,
                evt.DocumentHashPre,
                evt.GenerateCertificate,
            };
            await AppendAsync(
                evt.TenantId,
                evt.SignatureRequestId,
                SignatureAuditEventKind.RequestCompleted,
                evt.CompletedAtUtc,
                payload,
                appender,
                unitOfWork,
                logger,
                ct
            );
        }
    }

    public static async Task Handle(
        SignatureRequestSealedIntegrationEvent evt,
        IAuditChainAppender appender,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SignatureAuditEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(evt.CorrelationId))
        {
            var payload = new
            {
                evt.SealedFileId,
                evt.DocumentHashPost,
                evt.CertificateFileId,
                evt.SealedAtUtc,
            };
            await AppendAsync(
                evt.TenantId,
                evt.SignatureRequestId,
                SignatureAuditEventKind.RequestSealed,
                evt.SealedAtUtc,
                payload,
                appender,
                unitOfWork,
                logger,
                ct
            );
        }
    }

    public static async Task Handle(
        PreparerSignedIntegrationEvent evt,
        IAuditChainAppender appender,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SignatureAuditEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(evt.CorrelationId))
        {
            var payload = new
            {
                evt.PreparerUserId,
                evt.PtinOrEfin,
                evt.PreparerDisplayName,
                evt.SignedAtUtc,
            };
            await AppendAsync(
                evt.TenantId,
                evt.SignatureRequestId,
                SignatureAuditEventKind.PreparerSigned,
                evt.SignedAtUtc,
                payload,
                appender,
                unitOfWork,
                logger,
                ct
            );
        }
    }

    // ------------------------------------------------------------------
    // Helper compartido — una responsabilidad: persistir un append.
    // ------------------------------------------------------------------

    private static async Task AppendAsync(
        Guid tenantId,
        Guid signatureRequestId,
        SignatureAuditEventKind kind,
        DateTime occurredAtUtc,
        object payload,
        IAuditChainAppender appender,
        IUnitOfWork unitOfWork,
        ILogger logger,
        CancellationToken ct
    )
    {
        var result = await appender.AppendAsync(tenantId, signatureRequestId, kind, occurredAtUtc, payload, ct);
        if (result.IsFailure)
        {
            logger.LogWarning(
                "Audit chain append failed for request {RequestId} kind {Kind}: {Error}",
                signatureRequestId,
                kind,
                result.Error.Message
            );
            return;
        }
        await unitOfWork.SaveChangesAsync(ct);
    }
}
