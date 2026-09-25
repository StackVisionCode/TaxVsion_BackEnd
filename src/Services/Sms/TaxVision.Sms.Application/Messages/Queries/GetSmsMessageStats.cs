using Microsoft.Extensions.Options;
using TaxVision.Sms.Application.Abstractions;

namespace TaxVision.Sms.Application.Messages.Queries;

/// <summary>Conteos agregados por estado en la ventana [FromUtc, ToUtc] + bajas vigentes del tenant.
/// ActorUserId + CanViewAll: mismas stats acotadas a los clientes del actor cuando no ve todo (visibilidad P2).</summary>
public sealed record GetSmsStatsQuery(
    Guid TenantId,
    DateTime FromUtc,
    DateTime ToUtc,
    string? SourceContext,
    Guid ActorUserId,
    bool CanViewAll
);

public static class GetSmsStatsHandler
{
    public static Task<SmsStatsResponse> Handle(
        GetSmsStatsQuery query,
        ISmsReadService reader,
        IOptions<SmsVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        return reader.GetStatsAsync(query.TenantId, query.FromUtc, query.ToUtc, query.SourceContext, assignedTo, ct);
    }
}
