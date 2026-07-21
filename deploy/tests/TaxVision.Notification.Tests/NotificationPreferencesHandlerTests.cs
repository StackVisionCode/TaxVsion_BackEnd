using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Notifications.Preferences;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Tests;

public sealed class NotificationPreferencesHandlerTests
{
    [Fact]
    public async Task Get_returns_the_full_category_by_channel_grid_with_defaults()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var repository = new InMemoryUserNotificationPreferenceRepository();

        var result = await GetNotificationPreferencesHandler.Handle(
            new GetNotificationPreferencesQuery(tenantId, userId),
            repository,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var categories = Enum.GetValues<NotificationCategory>().Length;
        var channels = Enum.GetValues<NotificationChannel>().Length;
        Assert.Equal(categories * channels, result.Value.Count);
        Assert.All(result.Value, item => Assert.True(item.Enabled));
        Assert.All(
            result.Value.Where(item => item.Category == NotificationCategory.AccountSecurity),
            item => Assert.True(item.Locked)
        );
        Assert.All(
            result.Value.Where(item => item.Category != NotificationCategory.AccountSecurity),
            item => Assert.False(item.Locked)
        );
    }

    [Fact]
    public async Task Get_reflects_an_explicit_opt_out_for_its_own_category_and_channel_only()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var repository = new InMemoryUserNotificationPreferenceRepository();
        await repository.AddAsync(
            UserNotificationPreference
                .Create(tenantId, userId, NotificationCategory.StorageAndQuota, NotificationChannel.Email, false)
                .Value
        );

        var result = await GetNotificationPreferencesHandler.Handle(
            new GetNotificationPreferencesQuery(tenantId, userId),
            repository,
            CancellationToken.None
        );

        var disabled = result.Value.Single(item =>
            item.Category == NotificationCategory.StorageAndQuota && item.Channel == NotificationChannel.Email
        );
        Assert.False(disabled.Enabled);

        var stillEnabled = result.Value.Single(item =>
            item.Category == NotificationCategory.StorageAndQuota && item.Channel == NotificationChannel.InApp
        );
        Assert.True(stillEnabled.Enabled);
    }

    [Fact]
    public async Task Set_fails_for_a_locked_category()
    {
        var repository = new InMemoryUserNotificationPreferenceRepository();
        var unitOfWork = new NoOpUnitOfWork();

        var result = await SetNotificationPreferenceHandler.Handle(
            new SetNotificationPreferenceCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                NotificationCategory.AccountSecurity,
                NotificationChannel.Email,
                false
            ),
            repository,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("UserNotificationPreference.CategoryLocked", result.Error.Code);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Set_updates_an_existing_row_instead_of_duplicating_it()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var repository = new InMemoryUserNotificationPreferenceRepository();
        var unitOfWork = new NoOpUnitOfWork();

        await SetNotificationPreferenceHandler.Handle(
            new SetNotificationPreferenceCommand(
                tenantId,
                userId,
                NotificationCategory.Billing,
                NotificationChannel.Push,
                false
            ),
            repository,
            unitOfWork,
            CancellationToken.None
        );
        await SetNotificationPreferenceHandler.Handle(
            new SetNotificationPreferenceCommand(
                tenantId,
                userId,
                NotificationCategory.Billing,
                NotificationChannel.Push,
                true
            ),
            repository,
            unitOfWork,
            CancellationToken.None
        );

        var stored = await repository.ListForUserAsync(tenantId, userId, CancellationToken.None);
        var row = Assert.Single(stored);
        Assert.True(row.Enabled);
    }

    private sealed class InMemoryUserNotificationPreferenceRepository : IUserNotificationPreferenceRepository
    {
        private readonly List<UserNotificationPreference> _preferences = new();

        public Task<bool> IsEnabledAsync(
            Guid tenantId,
            Guid userId,
            NotificationCategory category,
            NotificationChannel channel,
            CancellationToken ct = default
        )
        {
            var match = Find(tenantId, userId, category, channel);
            return Task.FromResult(match?.Enabled ?? true);
        }

        public Task<UserNotificationPreference?> GetAsync(
            Guid tenantId,
            Guid userId,
            NotificationCategory category,
            NotificationChannel channel,
            CancellationToken ct = default
        ) => Task.FromResult(Find(tenantId, userId, category, channel));

        public Task<IReadOnlyList<UserNotificationPreference>> ListForUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<UserNotificationPreference>>(
                _preferences.Where(p => p.TenantId == tenantId && p.UserId == userId).ToList()
            );

        public Task AddAsync(UserNotificationPreference preference, CancellationToken ct = default)
        {
            _preferences.Add(preference);
            return Task.CompletedTask;
        }

        private UserNotificationPreference? Find(
            Guid tenantId,
            Guid userId,
            NotificationCategory category,
            NotificationChannel channel
        ) =>
            _preferences.FirstOrDefault(p =>
                p.TenantId == tenantId && p.UserId == userId && p.Category == category && p.Channel == channel
            );
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveCount++;
            return Task.FromResult(0);
        }
    }
}
