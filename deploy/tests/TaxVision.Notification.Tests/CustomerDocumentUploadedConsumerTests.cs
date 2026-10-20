using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.NotificationIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

public sealed class CustomerDocumentUploadedConsumerTests
{
    [Fact]
    public async Task Notifies_only_assigned_users_that_pass_capability_validation()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var assigned = Guid.NewGuid();
        var filteredOut = Guid.NewGuid();
        var harness = new NotificationDispatcherHarness();

        await CustomerDocumentUploadedConsumer.Handle(
            new CustomerDocumentUploadedIntegrationEvent
            {
                TenantId = tenantId,
                CustomerId = customerId,
                FileId = Guid.NewGuid(),
                FolderId = Guid.NewGuid(),
                OccurredOn = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc),
            },
            harness.Dispatcher,
            new Assignments(assigned, filteredOut),
            new Resolver(assigned),
            new TestCorrelationContext(),
            CancellationToken.None
        );

        var evt = Assert.IsType<InAppNotificationRequestedIntegrationEvent>(Assert.Single(harness.Publisher.Published));
        Assert.Equal(assigned, evt.RecipientUserId);
        Assert.Equal(NotificationKinds.CloudStorageCustomerDocumentUploaded, evt.Kind);
        Assert.Equal($"customer_uploads:{customerId:N}:20261018", evt.GroupKey);
    }

    [Fact]
    public async Task Does_not_notify_when_customer_has_no_assigned_preparer()
    {
        var harness = new NotificationDispatcherHarness();

        await CustomerDocumentUploadedConsumer.Handle(
            new CustomerDocumentUploadedIntegrationEvent
            {
                TenantId = Guid.NewGuid(),
                CustomerId = Guid.NewGuid(),
                FileId = Guid.NewGuid(),
            },
            harness.Dispatcher,
            new Assignments(),
            new Resolver(Guid.NewGuid()),
            new TestCorrelationContext(),
            CancellationToken.None
        );

        Assert.Empty(harness.Publisher.Published);
    }

    private sealed class Assignments(params Guid[] users) : ICustomerAssignmentAudienceRepository
    {
        public Task<IReadOnlyCollection<Guid>> GetAssignedUserIdsAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyCollection<Guid>>(users);
    }

    private sealed class Resolver(params Guid[] allowed) : IRecipientResolver
    {
        public Task<IReadOnlyList<Guid>> ResolveAsync(ByPermission audience, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<Result<IReadOnlyList<Guid>>> ResolveAsync(
            NotificationAudience audience,
            RecipientValidation validation,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success<IReadOnlyList<Guid>>(allowed));
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
