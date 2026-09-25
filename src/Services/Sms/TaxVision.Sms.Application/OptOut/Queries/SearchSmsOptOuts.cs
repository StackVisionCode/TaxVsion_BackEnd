using BuildingBlocks.Common;
using Microsoft.Extensions.Options;
using TaxVision.Sms.Application.Abstractions;

namespace TaxVision.Sms.Application.OptOut.Queries;

/// <summary>Bajas (opt-outs) del tenant, paginadas + filtro por consentimiento y texto (cliente/teléfono).
/// ActorUserId + CanViewAll: visibilidad por asignación (P2) — quien no ve todo solo ve las bajas de sus clientes.</summary>
public sealed record SearchSmsOptOutsQuery(
    Guid TenantId,
    SmsOptOutStatusFilter Status,
    string? Term,
    int Page,
    int Size,
    Guid ActorUserId,
    bool CanViewAll
);

public static class SearchSmsOptOutsHandler
{
    public static Task<PagedResult<SmsOptOutSummaryResponse>> Handle(
        SearchSmsOptOutsQuery query,
        ISmsReadService reader,
        IOptions<SmsVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        return reader.SearchOptOutsAsync(
            query.TenantId,
            query.Status,
            query.Term,
            query.Page,
            query.Size,
            assignedTo,
            ct
        );
    }
}
