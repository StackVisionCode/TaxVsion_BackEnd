using BuildingBlocks.Contracts.Work;
using Microsoft.Extensions.Options;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers;
using TaxVision.Customer.Application.Customers.Queries.Work;
using TaxVision.Customer.Domain.Customers;

namespace TaxVision.Customer.Tests.Application;

/// <summary>
/// Proveedor de trabajo de Customer: conteos clients-preparer / clients-assigned y top con el ABAC del que mira
/// (con el flag encendido y sin view_all, solo sus clientes asignados).
/// </summary>
public sealed class CustomerWorkTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly Guid OtherId = Guid.NewGuid();

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public async Task Work_applies_the_viewer_abac(bool flagEnabled, bool canViewAll, bool restricted)
    {
        var reads = new FakeWorkReads();

        await GetCustomerWorkHandler.Handle(
            new GetCustomerWorkQuery(TenantId, OtherId, ActorId, canViewAll),
            reads,
            Visibility(flagEnabled),
            CancellationToken.None
        );

        Assert.Equal(restricted ? ActorId : null, reads.VisibleTo);
        Assert.Equal(TenantId, reads.TenantId);
    }

    [Fact]
    public async Task Work_returns_both_counts_and_the_top_with_crm_links()
    {
        var reads = new FakeWorkReads
        {
            Counts = new CustomerWorkCounts(7, 9),
            Top = Enumerable
                .Range(0, 6)
                .Select(i => new CustomerWorkRow(Guid.NewGuid(), $"Client {i}", CustomerStatus.Active))
                .ToList(),
        };

        var work = await GetCustomerWorkHandler.Handle(
            new GetCustomerWorkQuery(TenantId, ActorId, ActorId, true),
            reads,
            Visibility(true),
            CancellationToken.None
        );

        Assert.Equal(CustomerWork.Provider, work.Provider);
        Assert.Equal(CustomerWork.Module, work.Module);
        Assert.Equal(
            [
                new WorkCount(WorkKinds.ClientsPreparer, 7, 0, 0, 0),
                new WorkCount(WorkKinds.ClientsAssigned, 9, 0, 0, 0),
            ],
            work.Counts
        );
        Assert.Equal(WorkLimits.TopItems, reads.Take);
        Assert.Equal(WorkLimits.TopItems, work.Top.Count);
        var first = work.Top[0];
        var row = reads.Top[0];
        Assert.Equal(
            new WorkItem(
                row.Id,
                WorkKinds.ClientsPreparer,
                "Client 0",
                row.Id,
                null,
                null,
                "Active",
                $"/clients/{row.Id}"
            ),
            first
        );
    }

    [Fact]
    public async Task Batch_returns_one_row_per_requested_user_with_abac()
    {
        var reads = new FakeWorkReads { ByUser = new Dictionary<Guid, int> { [OtherId] = 4 } };

        var batch = await GetCustomerWorkHandler.Handle(
            new GetCustomerWorkBatchQuery(TenantId, [ActorId, OtherId], ActorId, false),
            reads,
            Visibility(true),
            CancellationToken.None
        );

        Assert.Equal([new WorkBatchRow(ActorId, 0, 0), new WorkBatchRow(OtherId, 4, 0)], batch.Rows);
        Assert.Equal(ActorId, reads.VisibleTo);
        Assert.Equal([ActorId, OtherId], reads.UserIds);
    }

    [Fact]
    public void UserIds_parse_trims_dedupes_and_limits()
    {
        var parsed = CustomerWorkUserIds.Parse($" {ActorId} ,{OtherId},{ActorId},");

        Assert.True(parsed.IsSuccess);
        Assert.Equal([ActorId, OtherId], parsed.Value);
        Assert.True(
            CustomerWorkUserIds.Parse(string.Join(',', Enumerable.Range(0, 100).Select(_ => Guid.NewGuid()))).IsSuccess
        );
        Assert.True(
            CustomerWorkUserIds.Parse(string.Join(',', Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()))).IsFailure
        );
        Assert.True(CustomerWorkUserIds.Parse($"{ActorId},nope").IsFailure);
        Assert.True(CustomerWorkUserIds.Parse(" , ").IsFailure);
    }

    [Fact]
    public void Staff_filter_targets_others_only_when_an_id_is_someone_else()
    {
        Assert.Null(CustomerStaffFilter.From(null, null));
        Assert.Equal(new CustomerStaffFilter(ActorId, null), CustomerStaffFilter.From(ActorId, null));
        Assert.False(new CustomerStaffFilter(ActorId, ActorId).TargetsOtherThan(ActorId));
        Assert.True(new CustomerStaffFilter(ActorId, OtherId).TargetsOtherThan(ActorId));
        Assert.True(new CustomerStaffFilter(OtherId, null).TargetsOtherThan(ActorId));
    }

    private static IOptions<CustomerVisibilityOptions> Visibility(bool enabled) =>
        Options.Create(new CustomerVisibilityOptions { Enabled = enabled });

    private sealed class FakeWorkReads : ICustomerWorkReadService
    {
        public CustomerWorkCounts Counts { get; init; } = new(0, 0);
        public IReadOnlyList<CustomerWorkRow> Top { get; init; } = [];
        public IReadOnlyDictionary<Guid, int> ByUser { get; init; } = new Dictionary<Guid, int>();

        public Guid? TenantId { get; private set; }
        public Guid? VisibleTo { get; private set; }
        public int Take { get; private set; }
        public IReadOnlyList<Guid> UserIds { get; private set; } = [];

        public Task<CustomerWorkCounts> CountAsync(Guid tenantId, Guid userId, Guid? visibleTo, CancellationToken ct)
        {
            (TenantId, VisibleTo) = (tenantId, visibleTo);
            return Task.FromResult(Counts);
        }

        public Task<IReadOnlyList<CustomerWorkRow>> TopAsPreparerAsync(
            Guid tenantId,
            Guid userId,
            Guid? visibleTo,
            int take,
            CancellationToken ct
        )
        {
            Take = take;
            return Task.FromResult<IReadOnlyList<CustomerWorkRow>>(Top.Take(take).ToList());
        }

        public Task<IReadOnlyDictionary<Guid, int>> CountAsPreparerAsync(
            Guid tenantId,
            IReadOnlyList<Guid> userIds,
            Guid? visibleTo,
            CancellationToken ct
        )
        {
            (TenantId, VisibleTo, UserIds) = (tenantId, visibleTo, userIds);
            return Task.FromResult(ByUser);
        }
    }
}
