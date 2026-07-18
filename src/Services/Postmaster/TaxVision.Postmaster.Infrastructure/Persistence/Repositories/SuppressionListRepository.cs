using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Postmaster.Application.Suppression;
using TaxVision.Postmaster.Domain.Suppression;

namespace TaxVision.Postmaster.Infrastructure.Persistence.Repositories;

public sealed class SuppressionListRepository(PostmasterDbContext dbContext) : ISuppressionListRepository
{
    public async Task AddAsync(SuppressionListEntry entry, CancellationToken ct = default) =>
        await dbContext.SuppressionListEntries.AddAsync(entry, ct);

    public async Task<Result<SuppressionListEntry>> GetByAddressAsync(
        Guid tenantId,
        string emailAddress,
        CancellationToken ct = default
    )
    {
        var normalized = SuppressionListEntry.NormalizeAddress(emailAddress);
        var entry = normalized is null
            ? null
            : await dbContext.SuppressionListEntries.FirstOrDefaultAsync(
                e => e.TenantId == tenantId && e.EmailAddress == normalized,
                ct
            );

        return entry is null
            ? Result.Failure<SuppressionListEntry>(
                new Error("SuppressionListEntry.NotFound", $"'{emailAddress}' is not suppressed.")
            )
            : Result.Success(entry);
    }

    public async Task<IReadOnlyList<SuppressionListEntry>> ListAsync(
        Guid tenantId,
        string? addressFilter,
        SuppressionReason? reasonFilter,
        int page,
        int pageSize,
        CancellationToken ct = default
    )
    {
        var query = dbContext.SuppressionListEntries.Where(e => e.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(addressFilter))
            query = query.Where(e => e.EmailAddress.Contains(addressFilter.ToLowerInvariant()));

        if (reasonFilter is { } reason)
            query = query.Where(e => e.Reason == reason);

        return await query
            .OrderByDescending(e => e.AddedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlySet<string>> GetSuppressedAsync(
        Guid tenantId,
        IReadOnlyCollection<string> normalizedAddresses,
        CancellationToken ct = default
    )
    {
        if (normalizedAddresses.Count == 0)
            return new HashSet<string>();

        var suppressed = await dbContext
            .SuppressionListEntries.Where(e => e.TenantId == tenantId && normalizedAddresses.Contains(e.EmailAddress))
            .Select(e => e.EmailAddress)
            .ToListAsync(ct);

        return suppressed.ToHashSet();
    }

    public async Task<bool> RemoveAsync(Guid tenantId, string emailAddress, CancellationToken ct = default)
    {
        var normalized = SuppressionListEntry.NormalizeAddress(emailAddress);
        if (normalized is null)
            return false;

        var entry = await dbContext.SuppressionListEntries.FirstOrDefaultAsync(
            e => e.TenantId == tenantId && e.EmailAddress == normalized,
            ct
        );
        if (entry is null)
            return false;

        dbContext.SuppressionListEntries.Remove(entry);
        return true;
    }
}
