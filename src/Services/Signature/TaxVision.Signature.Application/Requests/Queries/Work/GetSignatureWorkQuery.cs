using BuildingBlocks.Contracts.Work;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Application.Requests.Queries.Work;

/// <summary>Trabajo de firmas de <paramref name="UserId"/> visto por <paramref name="ActorUserId"/> (su ABAC).</summary>
public sealed record GetSignatureWorkQuery(Guid TenantId, Guid UserId, Guid ActorUserId, bool CanViewAll);

/// <summary>Conteos por persona para la tabla de miembros, con el ABAC de quien mira.</summary>
public sealed record GetSignatureWorkBatchQuery(
    Guid TenantId,
    IReadOnlyList<Guid> UserIds,
    Guid ActorUserId,
    bool CanViewAll
);

public static class GetSignatureWorkHandler
{
    public static async Task<WorkSummaryResponse> Handle(
        GetSignatureWorkQuery query,
        ISignatureWorkReadService reads,
        IOfficeTimeZone zones,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        var window = await WindowAsync(query.TenantId, zones, clock, ct);
        var rows = await reads.ListAsync(
            new SignatureWorkFilter(
                query.TenantId,
                [query.UserId],
                query.ActorUserId,
                query.CanViewAll,
                window.ExpiredSinceUtc
            ),
            ct
        );
        return SignatureWork.Summarize(query.UserId, rows, window);
    }

    public static async Task<WorkBatchResponse> Handle(
        GetSignatureWorkBatchQuery query,
        ISignatureWorkReadService reads,
        IOfficeTimeZone zones,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        var window = await WindowAsync(query.TenantId, zones, clock, ct);
        var rows = await reads.ListAsync(
            new SignatureWorkFilter(
                query.TenantId,
                query.UserIds,
                query.ActorUserId,
                query.CanViewAll,
                window.ExpiredSinceUtc
            ),
            ct
        );
        return SignatureWork.Batch(query.UserIds, rows, window);
    }

    private static async Task<SignatureWorkWindow> WindowAsync(
        Guid tenantId,
        IOfficeTimeZone zones,
        TimeProvider clock,
        CancellationToken ct
    ) => SignatureWorkWindow.For(clock.GetUtcNow().UtcDateTime, await zones.ResolveAsync(tenantId, ct));
}
