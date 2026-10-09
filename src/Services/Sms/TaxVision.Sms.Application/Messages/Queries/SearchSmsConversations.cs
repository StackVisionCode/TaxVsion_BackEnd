using BuildingBlocks.Common;
using Microsoft.Extensions.Options;
using TaxVision.Sms.Application.Abstractions;

namespace TaxVision.Sms.Application.Messages.Queries;

/// <summary>Vista de conversaciones: una fila por cliente (último mensaje + total del hilo), paginada y
/// filtrable por texto. ActorUserId + CanViewAll: misma visibilidad por asignación (P2) que el historial.</summary>
public sealed record SearchSmsConversationsQuery(
    Guid TenantId,
    string? Term,
    string? SourceContext,
    int Page,
    int Size,
    Guid ActorUserId,
    bool CanViewAll
);

public static class SearchSmsConversationsHandler
{
    public static Task<PagedResult<SmsConversationSummaryResponse>> Handle(
        SearchSmsConversationsQuery query,
        ISmsReadService reader,
        IOptions<SmsVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        return reader.SearchConversationsAsync(
            query.TenantId,
            query.Term,
            query.SourceContext,
            query.Page,
            query.Size,
            assignedTo,
            ct
        );
    }
}
