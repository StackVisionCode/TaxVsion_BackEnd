using BuildingBlocks.Common;
using Microsoft.Extensions.Options;
using TaxVision.Sms.Application.Abstractions;

namespace TaxVision.Sms.Application.Messages.Queries;

/// <summary>Historial de SMS paginado + filtros (cliente, estado, texto/teléfono, rango de fechas).
/// ActorUserId + CanViewAll: visibilidad por asignación (P2) — quien no ve todo solo ve los SMS de sus clientes.</summary>
public sealed record SearchSmsMessagesQuery(
    Guid TenantId,
    Guid? CustomerId,
    SmsMessageStatusFilter Status,
    string? Term,
    DateTime? FromUtc,
    DateTime? ToUtc,
    string? SourceContext,
    int Page,
    int Size,
    Guid ActorUserId,
    bool CanViewAll
);

public static class SearchSmsMessagesHandler
{
    public static Task<PagedResult<SmsMessageSummaryResponse>> Handle(
        SearchSmsMessagesQuery query,
        ISmsReadService reader,
        IOptions<SmsVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        return reader.SearchMessagesAsync(
            query.TenantId,
            query.CustomerId,
            query.Status,
            query.Term,
            query.FromUtc,
            query.ToUtc,
            query.SourceContext,
            query.Page,
            query.Size,
            assignedTo,
            ct
        );
    }
}
