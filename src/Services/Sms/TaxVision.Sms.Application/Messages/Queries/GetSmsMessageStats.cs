using TaxVision.Sms.Application.Abstractions;

namespace TaxVision.Sms.Application.Messages.Queries;

/// <summary>Conteos agregados por estado en la ventana [FromUtc, ToUtc] + bajas vigentes del tenant.</summary>
public sealed record GetSmsStatsQuery(Guid TenantId, DateTime FromUtc, DateTime ToUtc, string? SourceContext);

public static class GetSmsStatsHandler
{
    public static Task<SmsStatsResponse> Handle(GetSmsStatsQuery query, ISmsReadService reader, CancellationToken ct) =>
        reader.GetStatsAsync(query.TenantId, query.FromUtc, query.ToUtc, query.SourceContext, ct);
}
