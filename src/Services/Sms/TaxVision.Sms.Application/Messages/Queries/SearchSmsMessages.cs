using BuildingBlocks.Common;
using TaxVision.Sms.Application.Abstractions;

namespace TaxVision.Sms.Application.Messages.Queries;

/// <summary>Historial de SMS paginado + filtros (cliente, estado, texto/teléfono, rango de fechas).</summary>
public sealed record SearchSmsMessagesQuery(
    Guid TenantId,
    Guid? CustomerId,
    SmsMessageStatusFilter Status,
    string? Term,
    DateTime? FromUtc,
    DateTime? ToUtc,
    string? SourceContext,
    int Page,
    int Size
);

public static class SearchSmsMessagesHandler
{
    public static Task<PagedResult<SmsMessageSummaryResponse>> Handle(
        SearchSmsMessagesQuery query,
        ISmsReadService reader,
        CancellationToken ct
    ) =>
        reader.SearchMessagesAsync(
            query.TenantId,
            query.CustomerId,
            query.Status,
            query.Term,
            query.FromUtc,
            query.ToUtc,
            query.SourceContext,
            query.Page,
            query.Size,
            ct
        );
}
