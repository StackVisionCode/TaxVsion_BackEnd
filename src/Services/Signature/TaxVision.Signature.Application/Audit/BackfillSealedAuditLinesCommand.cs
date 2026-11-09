using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Audit;

namespace TaxVision.Signature.Application.Audit;

/// <summary>Resultado del backfill: documentos sin línea y líneas agregadas.</summary>
public sealed record SealedAuditBackfillReport(bool DryRun, int MissingLines, int LinesAdded);

/// <summary>
/// Reposición SIG-2 (una sola vez, D-SIG-2): agrega la línea <c>RequestSealed</c> que le falta a la cadena de
/// los documentos ya sellados. Solo auditoría: no publica nada, así que nadie recibe un aviso tardío.
/// Idempotente: la consulta excluye los documentos que ya tienen su línea. Sin <paramref name="TenantId"/>,
/// todas las oficinas. Con <paramref name="DryRun"/> solo cuenta.
/// </summary>
public sealed record BackfillSealedAuditLinesCommand(Guid? TenantId, bool DryRun);

public static class BackfillSealedAuditLinesHandler
{
    public static async Task<Result<SealedAuditBackfillReport>> Handle(
        BackfillSealedAuditLinesCommand command,
        ISealedAuditGapQuery gaps,
        IAuditChainAppender appender,
        IUnitOfWork unitOfWork,
        ILogger<BackfillSealedAuditLinesCommand> logger,
        CancellationToken ct
    )
    {
        var missing = await gaps.ListAsync(command.TenantId, ct);
        if (command.DryRun || missing.Count == 0)
            return Result.Success(new SealedAuditBackfillReport(command.DryRun, missing.Count, 0));

        var added = 0;
        foreach (var gap in missing.OrderBy(gap => gap.SealedAtUtc))
        {
            // Mismo payload que el consumidor de SignatureDocumentSealed, marcado como agregado después.
            var payload = new
            {
                gap.DocumentId,
                gap.SealedFileId,
                gap.DocumentHashPost,
                gap.CertificateFileId,
                gap.SealedAtUtc,
                Backfilled = true,
            };
            var appended = await appender.AppendAsync(
                gap.TenantId,
                gap.SignatureRequestId,
                SignatureAuditEventKind.RequestSealed,
                gap.SealedAtUtc,
                payload,
                ct
            );
            if (appended.IsFailure)
            {
                logger.LogWarning(
                    "Sealed audit backfill skipped document {DocumentId} of request {RequestId}: {Error}",
                    gap.DocumentId,
                    gap.SignatureRequestId,
                    appended.Error.Message
                );
                continue;
            }
            added++;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(new SealedAuditBackfillReport(command.DryRun, missing.Count, added));
    }
}
