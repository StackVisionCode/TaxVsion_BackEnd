using BuildingBlocks.Common;
using TaxVision.Sms.Application.Abstractions;

namespace TaxVision.Sms.Application.OptOut.Queries;

/// <summary>Bajas (opt-outs) del tenant, paginadas + filtro por consentimiento y texto (cliente/teléfono).</summary>
public sealed record SearchSmsOptOutsQuery(
    Guid TenantId,
    SmsOptOutStatusFilter Status,
    string? Term,
    int Page,
    int Size
);

public static class SearchSmsOptOutsHandler
{
    public static Task<PagedResult<SmsOptOutSummaryResponse>> Handle(
        SearchSmsOptOutsQuery query,
        ISmsReadService reader,
        CancellationToken ct
    ) => reader.SearchOptOutsAsync(query.TenantId, query.Status, query.Term, query.Page, query.Size, ct);
}
