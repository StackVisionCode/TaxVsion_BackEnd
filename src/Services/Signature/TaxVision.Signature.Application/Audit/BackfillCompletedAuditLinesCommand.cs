using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using Wolverine.Attributes;

namespace TaxVision.Signature.Application.Audit;

/// <summary>Una solicitud completada sin línea <c>RequestCompleted</c> y lo que se hizo con ella.</summary>
public sealed record CompletedAuditBackfillItem(
    Guid TenantId,
    Guid SignatureRequestId,
    DateTime CompletedAtUtc,
    int ExistingLines,
    string Outcome
);

/// <summary>Resultado del backfill SIG-17. <c>EmptyChains</c>: solicitudes cuya cadena empieza con la línea repuesta.</summary>
public sealed record CompletedAuditBackfillReport(
    bool DryRun,
    int MissingLines,
    int LinesAdded,
    int EmptyChains,
    IReadOnlyList<CompletedAuditBackfillItem> Items
);

/// <summary>
/// Reposición SIG-17 (D-SIG-17): agrega al final de la cadena la línea <c>RequestCompleted</c> que le falta a una
/// solicitud completada, con <c>Backfilled = true</c> y la fecha real de completado. Solo auditoría: no publica nada.
/// Sin <paramref name="TenantId"/>, todas las oficinas. <paramref name="DryRun"/> por defecto: solo informa.
/// </summary>
public sealed record BackfillCompletedAuditLinesCommand(Guid? TenantId, bool DryRun = true);

public static class BackfillCompletedAuditLinesHandler
{
    public const string WouldAppend = "WouldAppend";
    public const string Appended = "Appended";
    public const string AlreadyPresent = "AlreadyPresent";

    // Sin transacción del handler: cada solicitud es una unidad con la suya.
    [NonTransactional]
    public static async Task<Result<CompletedAuditBackfillReport>> Handle(
        BackfillCompletedAuditLinesCommand command,
        ICompletedAuditBackfillStore store,
        TimeProvider clock,
        ILogger<BackfillCompletedAuditLinesCommand> logger,
        CancellationToken ct
    )
    {
        var gaps = await store.ListAsync(command.TenantId, ct);
        var emptyChains = gaps.Count(gap => gap.ExistingLines == 0);
        if (command.DryRun)
            return Result.Success(
                new CompletedAuditBackfillReport(
                    true,
                    gaps.Count,
                    0,
                    emptyChains,
                    gaps.Select(gap => Item(gap, WouldAppend)).ToList()
                )
            );

        var backfilledAtUtc = clock.GetUtcNow().UtcDateTime;
        var items = new List<CompletedAuditBackfillItem>(gaps.Count);
        foreach (var gap in gaps)
        {
            // Mismo payload que el consumidor de SignatureRequestCompleted, marcado como agregado después.
            var payload = new
            {
                gap.CompletedAtUtc,
                gap.Documents,
                gap.GenerateCertificate,
                Backfilled = true,
                BackfilledAtUtc = backfilledAtUtc,
            };
            var appended = await store.AppendIfMissingAsync(gap, payload, ct);
            if (appended.IsFailure)
            {
                logger.LogWarning(
                    "Completed audit backfill skipped request {RequestId}: {Error}",
                    gap.SignatureRequestId,
                    appended.Error.Code
                );
                items.Add(Item(gap, $"Failed:{appended.Error.Code}"));
                continue;
            }
            items.Add(Item(gap, appended.Value ? Appended : AlreadyPresent));
        }

        return Result.Success(
            new CompletedAuditBackfillReport(
                false,
                gaps.Count,
                items.Count(item => item.Outcome == Appended),
                emptyChains,
                items
            )
        );
    }

    private static CompletedAuditBackfillItem Item(CompletedAuditGap gap, string outcome) =>
        new(gap.TenantId, gap.SignatureRequestId, gap.CompletedAtUtc, gap.ExistingLines, outcome);
}
