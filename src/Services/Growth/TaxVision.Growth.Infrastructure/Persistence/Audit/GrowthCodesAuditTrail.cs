using System.Diagnostics;
using System.Text.Json;
using BuildingBlocks.Common;
using TaxVision.Codes.Application.Abstractions;

namespace TaxVision.Growth.Infrastructure.Persistence.Audit;

/// <summary>Escribe en <c>audit.AuditEntries</c>; la fila se guarda con la unidad de trabajo del handler.</summary>
public sealed class GrowthCodesAuditTrail(GrowthDbContext dbContext, ICorrelationContext correlation) : ICodesAuditTrail
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Append(CodesAuditRecord record) =>
        dbContext.AuditEntries.Add(
            GrowthAuditEntry.Create(
                record.TenantId,
                "Codes",
                record.AggregateType,
                record.AggregateId,
                aggregateVersion: null,
                record.Action,
                record.ActorUserId.ToString(),
                record.ActorType,
                record.OccurredAtUtc,
                correlationId: correlation.CorrelationId,
                traceId: Activity.Current?.TraceId.ToString(),
                beforeJson: record.Before is null ? null : JsonSerializer.Serialize(record.Before, Json),
                afterJson: record.After is null ? null : JsonSerializer.Serialize(record.After, Json),
                reason: record.Reason
            )
        );
}
