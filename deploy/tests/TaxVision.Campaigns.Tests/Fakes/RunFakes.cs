using BuildingBlocks.Common;
using TaxVision.Campaigns.Application.Contacts.Abstractions;
using TaxVision.Campaigns.Application.Runs.Abstractions;
using TaxVision.Campaigns.Application.Senders.Abstractions;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Runs;
using TaxVision.Campaigns.Domain.Senders;

namespace TaxVision.Campaigns.Tests.Fakes;

internal sealed class FakeCampaignRunRepository : ICampaignRunRepository
{
    public List<CampaignRun> Runs { get; } = [];

    public Task<CampaignRun?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(Runs.Find(r => r.TenantId == tenantId && r.Id == id));

    public Task<CampaignRun?> GetByDispatchIdAsync(Guid tenantId, string dispatchId, CancellationToken ct = default) =>
        Task.FromResult(Runs.Find(r => r.TenantId == tenantId && r.Recipients.Any(x => x.DispatchId == dispatchId)));

    public Task<CampaignRun?> GetByRecipientIdAsync(Guid tenantId, Guid recipientId, CancellationToken ct = default) =>
        Task.FromResult(Runs.Find(r => r.TenantId == tenantId && r.Recipients.Any(x => x.Id == recipientId)));

    public Task<PagedResult<CampaignRun>> ListByCampaignAsync(
        Guid tenantId,
        Guid campaignId,
        int page,
        int size,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task AddAsync(CampaignRun run, CancellationToken ct = default)
    {
        Runs.Add(run);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<StaleRunRef>> ListStaleDispatchingAsync(
        DateTime createdBeforeUtc,
        int batchSize,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<StaleRunRef>>(
            Runs.Where(r => r.Status == CampaignRunStatus.Dispatching && r.CreatedAtUtc < createdBeforeUtc)
                .Take(batchSize)
                .Select(r => new StaleRunRef(r.TenantId, r.Id))
                .ToList()
        );

    /// <summary>Agendado de runs que no están en <see cref="Runs"/> (solo se conoce su ScheduleId).</summary>
    public Dictionary<Guid, Guid> ScheduleIdByRun { get; } = [];

    public Task<Guid?> GetScheduleIdAsync(Guid tenantId, Guid runId, CancellationToken ct = default) =>
        Task.FromResult(
            ScheduleIdByRun.TryGetValue(runId, out var scheduleId)
                ? scheduleId
                : Runs.Find(r => r.TenantId == tenantId && r.Id == runId)?.ScheduleId
        );
}

/// <summary>Wallet que autoriza siempre y anota cada referencia reservada.</summary>
internal sealed class FakeWalletSpendClient : IWalletSpendClient
{
    public List<Guid> ReservedReferences { get; } = [];

    public Task<WalletReserveResult> ReserveAsync(
        Guid tenantId,
        string referenceType,
        Guid referenceId,
        WalletUnitCounts units,
        string? callerBearerToken,
        CancellationToken ct = default
    )
    {
        ReservedReferences.Add(referenceId);
        return Task.FromResult(new WalletReserveResult(true, true, 0, 0, 0, "USD"));
    }

    public Task SettleAsync(
        Guid tenantId,
        string referenceType,
        Guid referenceId,
        int consumedUnits,
        CancellationToken ct = default
    ) => Task.CompletedTask;
}

internal sealed class FakeSenderProfileRepository : ISenderProfileRepository
{
    public Task<SenderProfile?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult<SenderProfile?>(null);

    public Task<IReadOnlyList<SenderProfile>> GetManyByIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<SenderProfile>>([]);

    public Task<PagedResult<SenderProfile>> ListAsync(
        Guid tenantId,
        CampaignChannel? channel,
        int page,
        int size,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task AddAsync(SenderProfile sender, CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class FakeCampaignCustomerAssignmentReader : ICampaignCustomerAssignmentReader
{
    public Task<IReadOnlyCollection<Guid>> GetAssignedCustomerIdsAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyCollection<Guid>>([]);
}
