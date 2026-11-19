using BuildingBlocks.Contracts.Work;
using Microsoft.Extensions.Options;
using TaxVision.Customer.Application.Abstractions;

namespace TaxVision.Customer.Application.Customers.Queries.Work;

/// <summary>Clientes de <paramref name="UserId"/> vistos por <paramref name="ActorUserId"/> (su ABAC).</summary>
public sealed record GetCustomerWorkQuery(Guid TenantId, Guid UserId, Guid ActorUserId, bool CanViewAll);

/// <summary>Clientes como preparador por persona, con el ABAC del que mira.</summary>
public sealed record GetCustomerWorkBatchQuery(
    Guid TenantId,
    IReadOnlyList<Guid> UserIds,
    Guid ActorUserId,
    bool CanViewAll
);

public static class GetCustomerWorkHandler
{
    public static async Task<WorkSummaryResponse> Handle(
        GetCustomerWorkQuery query,
        ICustomerWorkReadService reads,
        IOptions<CustomerVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var visibleTo = CustomerWork.VisibleTo(visibility.Value, query.ActorUserId, query.CanViewAll);
        var counts = await reads.CountAsync(query.TenantId, query.UserId, visibleTo, ct);
        var top = await reads.TopAsPreparerAsync(query.TenantId, query.UserId, visibleTo, WorkLimits.TopItems, ct);
        return CustomerWork.Summarize(counts, top);
    }

    public static async Task<WorkBatchResponse> Handle(
        GetCustomerWorkBatchQuery query,
        ICustomerWorkReadService reads,
        IOptions<CustomerVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var visibleTo = CustomerWork.VisibleTo(visibility.Value, query.ActorUserId, query.CanViewAll);
        var counts = await reads.CountAsPreparerAsync(query.TenantId, query.UserIds, visibleTo, ct);
        return CustomerWork.Batch(query.UserIds, counts);
    }
}
