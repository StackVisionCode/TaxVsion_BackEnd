namespace TaxVision.Tasks.ClientForms.Application.Participants.Prospects;

/// <summary>Resumen del widget: un conteo por estado en una consulta, con el recorte del listado.</summary>
public static class CountProspectsByStatusHandler
{
    public static async Task<ProspectStatusCountsResponse> Handle(
        CountProspectsByStatusQuery query,
        IProspectReadRepository reads,
        CancellationToken ct
    )
    {
        var counts = await reads.CountByStatusAsync(query.TenantId, query.ActorUserId, query.CanViewAll, ct);
        int Of(ProspectStatus status) => counts.GetValueOrDefault(status);
        return new ProspectStatusCountsResponse(
            Of(ProspectStatus.Invited),
            Of(ProspectStatus.Verified),
            Of(ProspectStatus.Submitted),
            Of(ProspectStatus.Converted),
            Of(ProspectStatus.Duplicate),
            Of(ProspectStatus.Lost)
        );
    }
}
