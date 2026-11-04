using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Directory;

namespace TaxVision.Notification.Tests.TestDoubles;

/// <summary>Directorio <c>userId → email</c> en memoria; vacío = sin señal de inactividad.</summary>
public sealed class InMemoryUserEmailDirectoryRepository : IUserEmailDirectoryRepository
{
    public List<UserEmailDirectoryEntry> Entries { get; } = [];

    public UserEmailDirectoryEntry Seed(Guid tenantId, Guid userId, string email, bool isActive = true)
    {
        var entry = UserEmailDirectoryEntry.Create(tenantId, userId, email, isActive);
        Entries.Add(entry);
        return entry;
    }

    public Task<UserEmailDirectoryEntry?> FindAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
        Task.FromResult(Entries.FirstOrDefault(e => e.TenantId == tenantId && e.UserId == userId));

    public Task AddAsync(UserEmailDirectoryEntry entry, CancellationToken ct = default)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
}
