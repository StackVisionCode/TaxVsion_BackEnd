using System.Text.Json;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.Catalog;

namespace TaxVision.Subscription.Application.Catalog.Audit;

/// <summary>Auditoría de plataforma del catálogo: solo agrega y consulta.</summary>
public interface ICatalogAuditLog
{
    Task AddAsync(CatalogAuditEntry entry, CancellationToken ct = default);

    /// <summary>Más nuevas primero.</summary>
    Task<IReadOnlyList<CatalogAuditEntry>> ListAsync(CatalogAuditFilter filter, CancellationToken ct = default);
}

public sealed record CatalogAuditFilter(string? EntityType, Guid? EntityId, DateTime? BeforeUtc, int Take);

/// <summary>Arma la entrada (antes y después en JSON) y la agrega en la misma transacción que el cambio.</summary>
public static class CatalogAudit
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public static async Task RecordAsync(
        ICatalogAuditLog log,
        CatalogAuditActor actor,
        string action,
        CatalogAuditTarget target,
        object? before,
        object? after,
        string? reason,
        string? correlationId,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var entry = CatalogAuditEntry.Record(
            actor,
            action,
            target,
            before is null ? null : JsonSerializer.Serialize(before, Json),
            after is null ? null : JsonSerializer.Serialize(after, Json),
            reason,
            correlationId,
            nowUtc
        );
        // Una entrada inválida es un error de programación (acción o entidad vacías): que falle la operación.
        if (entry.IsFailure)
            throw new InvalidOperationException($"{entry.Error.Code}: {entry.Error.Message}");

        await log.AddAsync(entry.Value, ct);
    }

    /// <summary>Solo audita si el estado cambió (un pedido repetido no deja ruido).</summary>
    public static Task RecordIfChangedAsync(
        ICatalogAuditLog log,
        CatalogAuditActor actor,
        string action,
        CatalogAuditTarget target,
        object before,
        object after,
        string? reason,
        string? correlationId,
        DateTime nowUtc,
        CancellationToken ct
    ) =>
        JsonSerializer.Serialize(before, Json) == JsonSerializer.Serialize(after, Json)
            ? Task.CompletedTask
            : RecordAsync(log, actor, action, target, before, after, reason, correlationId, nowUtc, ct);
}

public sealed record GetCatalogAuditQuery(string? EntityType, Guid? EntityId, DateTime? BeforeUtc, int Take = 50);

public sealed record CatalogAuditEntryResponse(
    Guid Id,
    DateTime OccurredAtUtc,
    string ActorKind,
    Guid? ActorUserId,
    string ActorLabel,
    string Action,
    string EntityType,
    Guid? EntityId,
    string? EntityCode,
    string? BeforeJson,
    string? AfterJson,
    string? Reason,
    string CorrelationId
);

public static class GetCatalogAuditHandler
{
    public const int MaxPageSize = 200;

    public static async Task<Result<IReadOnlyList<CatalogAuditEntryResponse>>> Handle(
        GetCatalogAuditQuery query,
        ICatalogAuditLog log,
        CancellationToken ct
    )
    {
        var take = Math.Clamp(query.Take, 1, MaxPageSize);
        var entries = await log.ListAsync(
            new CatalogAuditFilter(
                string.IsNullOrWhiteSpace(query.EntityType) ? null : query.EntityType.Trim(),
                query.EntityId,
                query.BeforeUtc,
                take
            ),
            ct
        );

        return Result.Success<IReadOnlyList<CatalogAuditEntryResponse>>(
            entries
                .Select(entry => new CatalogAuditEntryResponse(
                    entry.Id,
                    entry.OccurredAtUtc,
                    entry.ActorKind.ToString(),
                    entry.ActorUserId,
                    entry.ActorLabel,
                    entry.Action,
                    entry.EntityType,
                    entry.EntityId,
                    entry.EntityCode,
                    entry.BeforeJson,
                    entry.AfterJson,
                    entry.Reason,
                    entry.CorrelationId
                ))
                .ToList()
        );
    }
}
