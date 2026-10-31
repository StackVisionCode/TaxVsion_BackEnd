using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using BuildingBlocks.Messaging.NotificationIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

public sealed class OfficeAlertConsumersTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Blocked_by_policy_notifies_owner_and_subscribers_with_settings_manage_capability()
    {
        var owner = Guid.NewGuid();
        var subscriber = Guid.NewGuid();
        var uploader = Guid.NewGuid();
        var harness = new NotificationDispatcherHarness();
        var resolver = new AudienceResolver(
            owner,
            new Dictionary<string, IReadOnlyList<Guid>>
            {
                [NotificationPermissions.StorageAlerts] = [owner, subscriber],
            }
        );

        await FileBlockedByPolicyConsumer.Handle(
            new FileBlockedByPolicyIntegrationEvent
            {
                TenantId = TenantId,
                FileId = Guid.NewGuid(),
                ObjectKey = "tenant/.../file",
                PolicyReason = "nudity",
                CreatedBy = uploader,
            },
            harness.Dispatcher,
            AlwaysOnInAppSource.Instance,
            resolver,
            new TestCorrelationContext(),
            CancellationToken.None
        );

        AssertRecipients(harness, owner, subscriber);
        Assert.All(
            resolver.Validations,
            validation =>
            {
                Assert.Equal(CloudStoragePermissions.SettingsManage, validation.RequiredCapability);
                Assert.Equal(uploader, validation.ExcludeActorUserId);
            }
        );
    }

    [Fact]
    public async Task Blocked_by_policy_is_suppressed_when_gate_apunta_a_Communication()
    {
        var harness = new NotificationDispatcherHarness();
        var resolver = new AudienceResolver(Guid.NewGuid(), new Dictionary<string, IReadOnlyList<Guid>>());

        await FileBlockedByPolicyConsumer.Handle(
            new FileBlockedByPolicyIntegrationEvent
            {
                TenantId = TenantId,
                FileId = Guid.NewGuid(),
                ObjectKey = "k",
                PolicyReason = "nudity",
                CreatedBy = Guid.NewGuid(),
            },
            harness.Dispatcher,
            new StubInAppSource(publishes: false),
            resolver,
            new TestCorrelationContext(),
            CancellationToken.None
        );

        Assert.Empty(harness.Publisher.Published);
        Assert.Empty(resolver.Audiences);
    }

    [Fact]
    public async Task Pending_review_notifies_only_the_uploader_without_the_internal_reason()
    {
        var uploader = Guid.NewGuid();
        var harness = new NotificationDispatcherHarness();

        await FilePendingReviewConsumer.Handle(
            new FilePendingReviewIntegrationEvent
            {
                TenantId = TenantId,
                FileId = Guid.NewGuid(),
                ObjectKey = "k",
                Reason = "Possible sexual content (score 0.700).",
                CreatedBy = uploader,
            },
            harness.Dispatcher,
            AlwaysOnInAppSource.Instance,
            new TestCorrelationContext(),
            CancellationToken.None
        );

        AssertRecipients(harness, uploader);
        var row = harness.Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>().Single();
        Assert.Equal(NotificationKinds.CloudStorageFilePendingReview, row.Kind);
        Assert.DoesNotContain("score", System.Text.Json.JsonSerializer.Serialize(row));
    }

    [Fact]
    public async Task Pending_review_is_suppressed_without_uploader_or_when_the_gate_points_to_Communication()
    {
        var harness = new NotificationDispatcherHarness();
        var evt = new FilePendingReviewIntegrationEvent
        {
            TenantId = TenantId,
            FileId = Guid.NewGuid(),
            ObjectKey = "k",
            Reason = "x",
            CreatedBy = Guid.NewGuid(),
        };

        await FilePendingReviewConsumer.Handle(evt, harness.Dispatcher, new StubInAppSource(publishes: false), new TestCorrelationContext(), CancellationToken.None);
        await FilePendingReviewConsumer.Handle(evt with { CreatedBy = null }, harness.Dispatcher, AlwaysOnInAppSource.Instance, new TestCorrelationContext(), CancellationToken.None);

        Assert.Empty(harness.Publisher.Published);
    }

    [Fact]
    public async Task Dmca_takedown_resolves_by_legal_manage_capability_and_excludes_uploader()
    {
        var owner = Guid.NewGuid();
        var subscriber = Guid.NewGuid();
        var uploader = Guid.NewGuid();
        var harness = new NotificationDispatcherHarness();
        var resolver = new AudienceResolver(
            owner,
            new Dictionary<string, IReadOnlyList<Guid>>
            {
                [NotificationPermissions.StorageAlerts] = [owner, subscriber],
            }
        );

        await FileBlockedByDmcaTakedownConsumer.Handle(
            new FileBlockedByDmcaTakedownIntegrationEvent
            {
                TenantId = TenantId,
                FileId = Guid.NewGuid(),
                DmcaNoticeId = Guid.NewGuid(),
                CreatedBy = uploader,
            },
            harness.Dispatcher,
            AlwaysOnInAppSource.Instance,
            resolver,
            new TestCorrelationContext(),
            CancellationToken.None
        );

        AssertRecipients(harness, owner, subscriber);
        Assert.All(
            resolver.Validations,
            validation =>
            {
                Assert.Equal(CloudStoragePermissions.LegalManage, validation.RequiredCapability);
                Assert.Equal(uploader, validation.ExcludeActorUserId);
            }
        );
    }

    [Fact]
    public async Task Storage_alert_notifies_owner_and_subscribers_once()
    {
        var owner = Guid.NewGuid();
        var subscriber = Guid.NewGuid();
        var harness = new NotificationDispatcherHarness();
        var resolver = new AudienceResolver(
            owner,
            new Dictionary<string, IReadOnlyList<Guid>>
            {
                [NotificationPermissions.StorageAlerts] = [owner, subscriber],
            }
        );

        await StorageLimitExceededConsumer.Handle(
            new StorageLimitExceededIntegrationEvent
            {
                TenantId = TenantId,
                AttemptedFileSizeBytes = 10,
                UsedBytes = 90,
                ReservedBytes = 5,
                MaxBytes = 100,
            },
            harness.Dispatcher,
            resolver,
            new TestCorrelationContext(),
            CancellationToken.None
        );

        AssertRecipients(harness, owner, subscriber);
        Assert.All(
            resolver.Validations,
            validation =>
            {
                Assert.Equal(CloudStoragePermissions.SettingsManage, validation.RequiredCapability);
                Assert.NotNull(validation.RequiredModule);
            }
        );
    }

    [Fact]
    public async Task Office_mailbox_failure_notifies_creator_subscribers_and_tenant_owner_once()
    {
        // Política B — buzón de oficina: creator + subscribers + owner del tenant (admin).
        var creator = Guid.NewGuid();
        var subscriber = Guid.NewGuid();
        var tenantOwner = Guid.NewGuid();
        var harness = new NotificationDispatcherHarness();
        var resolver = new AudienceResolver(
            tenantOwner,
            new Dictionary<string, IReadOnlyList<Guid>>
            {
                [NotificationPermissions.MailboxAlerts] = [creator, subscriber],
            }
        );

        await ConnectorsOAuthRefreshFailedConsumer.Handle(
            new ConnectorsOAuthRefreshFailedIntegrationEvent
            {
                TenantId = TenantId,
                AccountId = Guid.NewGuid(),
                ConnectionId = Guid.NewGuid(),
                ProviderCode = "Google",
                Reason = "invalid_grant",
                ErrorCode = "invalid_grant",
                FailedAtUtc = DateTime.UtcNow,
                CreatedByUserId = creator,
                IsOfficeMailbox = true,
            },
            harness.Dispatcher,
            AlwaysOnInAppSource.Instance,
            resolver,
            new TestCorrelationContext(),
            CancellationToken.None
        );

        AssertRecipients(harness, creator, subscriber, tenantOwner);
        Assert.All(
            resolver.Validations,
            validation => Assert.Equal(ConnectorsPermissions.AccountsOfficeRead, validation.RequiredCapability)
        );
    }

    [Fact]
    public async Task Refresh_failure_groups_in_app_notifications_by_mailbox()
    {
        // Con GroupKey, Communication actualiza el aviso sin leer de esa cuenta en lugar de crear otro.
        var owner = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var harness = new NotificationDispatcherHarness();
        var resolver = new AudienceResolver(
            Guid.NewGuid(),
            new Dictionary<string, IReadOnlyList<Guid>> { [NotificationPermissions.MailboxAlerts] = [] }
        );

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await ConnectorsOAuthRefreshFailedConsumer.Handle(
                new ConnectorsOAuthRefreshFailedIntegrationEvent
                {
                    TenantId = TenantId,
                    AccountId = accountId,
                    ConnectionId = Guid.NewGuid(),
                    ProviderCode = "Gmail",
                    Reason = "invalid_grant",
                    ErrorCode = "invalid_grant",
                    FailedAtUtc = DateTime.UtcNow,
                    CreatedByUserId = owner,
                    IsOfficeMailbox = false,
                    MailboxOwnerUserId = owner,
                },
                harness.Dispatcher,
                AlwaysOnInAppSource.Instance,
                resolver,
                new TestCorrelationContext(),
                CancellationToken.None
            );
        }

        var published = harness.Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>().ToList();
        Assert.Equal(2, published.Count);
        Assert.All(
            published,
            notification => Assert.Equal($"connectors.oauth.refresh_failed:{accountId:N}", notification.GroupKey)
        );
    }

    [Fact]
    public async Task Personal_mailbox_failure_notifies_only_mailbox_owner()
    {
        var creator = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var harness = new NotificationDispatcherHarness();
        var resolver = new AudienceResolver(
            Guid.NewGuid(),
            new Dictionary<string, IReadOnlyList<Guid>> { [NotificationPermissions.MailboxAlerts] = [Guid.NewGuid()] }
        );

        await ConnectorsWatchExpiredConsumer.Handle(
            new ConnectorsWatchExpiredIntegrationEvent
            {
                TenantId = TenantId,
                AccountId = Guid.NewGuid(),
                SubscriptionId = Guid.NewGuid(),
                ProviderCode = "Microsoft",
                FailureCount = 3,
                ExpiredAtUtc = DateTime.UtcNow,
                CreatedByUserId = creator,
                IsOfficeMailbox = false,
                MailboxOwnerUserId = owner,
            },
            harness.Dispatcher,
            AlwaysOnInAppSource.Instance,
            resolver,
            new TestCorrelationContext(),
            CancellationToken.None
        );

        AssertRecipients(harness, owner);
        Assert.Single(resolver.Audiences);
        Assert.IsType<Participants>(resolver.Audiences[0]);
        Assert.Equal(ConnectorsPermissions.AccountsRead, resolver.Validations[0].RequiredCapability);
    }

    [Fact]
    public async Task Security_alert_deduplicates_affected_owner_and_subscribers()
    {
        var affectedOwner = Guid.NewGuid();
        var subscriber = Guid.NewGuid();
        var harness = new NotificationDispatcherHarness();
        var resolver = new AudienceResolver(
            affectedOwner,
            new Dictionary<string, IReadOnlyList<Guid>>
            {
                [NotificationPermissions.SecurityTenantAlerts] = [affectedOwner, subscriber],
            }
        );

        await SecurityAlertConsumer.Handle(
            new SecurityAlertIntegrationEvent
            {
                TenantId = TenantId,
                UserId = affectedOwner,
                AlertType = SecurityAlertType.AccountLockedOut,
            },
            harness.Dispatcher,
            Options.Create(new PortalOptions()),
            TestPlatform.Settings,
            resolver,
            new TestCorrelationContext(),
            CancellationToken.None
        );

        AssertRecipients(harness, affectedOwner, subscriber);
        Assert.All(
            resolver.Validations,
            validation => Assert.Equal(SubscriptionPermissions.AuditView, validation.RequiredCapability)
        );
    }

    private static void AssertRecipients(NotificationDispatcherHarness harness, params Guid[] expected)
    {
        var actual = harness
            .Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>()
            .Select(notification => notification.RecipientUserId)
            .Order()
            .ToArray();

        Assert.Equal(expected.Order().ToArray(), actual);
    }

    private sealed class AudienceResolver(Guid owner, IReadOnlyDictionary<string, IReadOnlyList<Guid>> permissions)
        : IRecipientResolver
    {
        public List<NotificationAudience> Audiences { get; } = [];
        public List<RecipientValidation> Validations { get; } = [];

        public Task<IReadOnlyList<Guid>> ResolveAsync(ByPermission audience, CancellationToken ct = default) =>
            Task.FromResult(ResolvePermission(audience.PermissionCode));

        public Task<Result<IReadOnlyList<Guid>>> ResolveAsync(
            NotificationAudience audience,
            RecipientValidation validation,
            CancellationToken ct = default
        )
        {
            Audiences.Add(audience);
            Validations.Add(validation);

            return Task.FromResult(
                Result.Success<IReadOnlyList<Guid>>(
                    audience switch
                    {
                        TenantOwnerAudience => [owner],
                        ByPermission permission => ResolvePermission(permission.PermissionCode),
                        Participants participants => participants.UserIds,
                        _ => [],
                    }
                )
            );
        }

        private IReadOnlyList<Guid> ResolvePermission(string permissionCode) =>
            permissions.TryGetValue(permissionCode, out var recipients) ? recipients : [];
    }

    private sealed class StubInAppSource(bool publishes) : IInAppSourceGate
    {
        public bool PublishesInApp(InAppSourceGroup group) => publishes;
    }

    private sealed class TestCorrelationContext : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = string.Empty;

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId)
        {
            CorrelationId = correlationId;
            return new Scope();
        }

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
