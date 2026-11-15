using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.FinancialInsightsIntegrationEvents;
using BuildingBlocks.Messaging.NotificationIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>
/// Lote de Financial Insights terminado: staff → aviso a quien lo subió; portal → aviso al cliente y a la
/// oficina asignada (o al owner si nadie está asignado).
/// </summary>
public sealed class FinancialImportBatchCompletedConsumerTests
{
    private const string Module = "financial_insights";

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly Guid BatchId = Guid.NewGuid();
    private static readonly Guid Creator = Guid.NewGuid();

    [Fact]
    public async Task Staff_batch_without_issues_notifies_the_creator_as_processed()
    {
        var harness = new NotificationDispatcherHarness();
        var resolver = new Resolver { Allowed = [Creator] };

        await Handle(harness, resolver, new Assignments(), Batch(FinancialImportBatchSources.Staff, completed: 3));

        var evt = Single(harness);
        Assert.Equal(Creator, evt.RecipientUserId);
        Assert.Equal("financial.import_batch.completed", evt.Kind);
        Assert.Equal("financial_import_batch", evt.ResourceType);
        Assert.Equal(BatchId.ToString(), evt.ResourceId);
        Assert.Equal(BatchId.ToString(), evt.ResourceContext["batchId"]);
        Assert.Equal(CustomerId.ToString(), evt.ResourceContext["customerId"]);
        Assert.Equal("Bank statements processed", evt.FallbackTitle);
        Assert.Equal("3 statements are ready to review.", evt.FallbackBody);
        Assert.Equal("3", evt.Params["completedDocuments"]);

        var validation = Assert.Single(resolver.Calls).Validation;
        Assert.Equal(FinancialInsightsPermissions.View, validation.RequiredCapability);
        Assert.Equal(Module, validation.RequiredModule);
    }

    [Fact]
    public async Task Staff_batch_with_issues_needs_attention_with_counts()
    {
        var harness = new NotificationDispatcherHarness();

        await Handle(
            harness,
            new Resolver { Allowed = [Creator] },
            new Assignments(),
            Batch(FinancialImportBatchSources.Staff, completed: 2, needsReview: 1, failed: 1, rejected: 1)
        );

        var evt = Single(harness);
        Assert.Equal("Bank statements need attention", evt.FallbackTitle);
        Assert.Equal("2 ready, 1 needs review, 1 failed, 1 rejected.", evt.FallbackBody);
        Assert.DoesNotContain("Financial", evt.FallbackTitle + evt.FallbackBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Staff_batch_is_skipped_when_module_or_permission_rejects_the_creator()
    {
        var harness = new NotificationDispatcherHarness();

        await Handle(
            harness,
            new Resolver { ModuleDisabled = true, Allowed = [Creator] },
            new Assignments(),
            Batch(FinancialImportBatchSources.Staff, completed: 1)
        );

        Assert.Empty(harness.Publisher.Published);
    }

    [Fact]
    public async Task Portal_batch_notifies_the_client_and_the_assigned_staff()
    {
        var harness = new NotificationDispatcherHarness();
        var assigned = Guid.NewGuid();
        var resolver = new Resolver { Allowed = [assigned], PortalUser = Creator };

        await Handle(
            harness,
            resolver,
            new Assignments(assigned, Guid.NewGuid()),
            Batch(FinancialImportBatchSources.Portal, completed: 1)
        );

        var events = harness.Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>().ToArray();
        Assert.Equal(2, events.Length);

        var client = Assert.Single(events, e => e.RecipientUserId == Creator);
        Assert.Equal("financial.portal_import_batch.completed", client.Kind);
        Assert.Equal("financial_import_batch", client.ResourceType);
        Assert.Equal(BatchId.ToString(), client.ResourceContext["batchId"]);
        Assert.Equal(CustomerId.ToString(), client.ResourceContext["customerId"]);
        Assert.Equal("Your bank statements were received and analyzed", client.FallbackTitle);

        var staff = Assert.Single(events, e => e.RecipientUserId == assigned);
        Assert.Equal("financial.portal_import_batch.submitted", staff.Kind);
        Assert.Equal("financial_import_batch", staff.ResourceType);
        Assert.Equal(BatchId.ToString(), staff.ResourceId);
        Assert.Equal(CustomerId.ToString(), staff.ResourceContext["customerId"]);
        Assert.Equal("Client bank statements ready to review", staff.FallbackTitle);
        Assert.Equal("1 statement is ready to review.", staff.FallbackBody);

        var staffValidation = Assert.Single(resolver.Calls, c => c.Audience is Participants).Validation;
        Assert.Equal(FinancialInsightsPermissions.View, staffValidation.RequiredCapability);
        Assert.Equal(Module, staffValidation.RequiredModule);
        Assert.Equal(
            Module,
            Assert.Single(resolver.Calls, c => c.Audience is CustomerPortal).Validation.RequiredModule
        );
    }

    [Fact]
    public async Task Portal_batch_with_failures_uses_the_attention_copy_without_technical_detail()
    {
        var harness = new NotificationDispatcherHarness();
        var resolver = new Resolver { Allowed = [Guid.NewGuid()], PortalUser = Creator };

        await Handle(
            harness,
            resolver,
            new Assignments(resolver.Allowed[0]),
            Batch(FinancialImportBatchSources.Portal, completed: 1, failed: 1)
        );

        var events = harness.Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>().ToArray();
        var client = Assert.Single(events, e => e.RecipientUserId == Creator);
        Assert.Equal("Some of your bank statements need attention", client.FallbackTitle);
        Assert.DoesNotContain("failed", client.FallbackBody ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        var staff = Assert.Single(events, e => e.RecipientUserId != Creator);
        Assert.Equal("Client bank statements need attention", staff.FallbackTitle);
        Assert.Equal("1 ready, 1 failed.", staff.FallbackBody);
    }

    [Fact]
    public async Task Portal_batch_without_assigned_staff_falls_back_to_the_owner()
    {
        var harness = new NotificationDispatcherHarness();
        var owner = Guid.NewGuid();
        var resolver = new Resolver { Owner = owner, PortalUser = Creator };

        await Handle(harness, resolver, new Assignments(), Batch(FinancialImportBatchSources.Portal, completed: 2));

        var staff = Assert.Single(
            harness.Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>(),
            e => e.RecipientUserId == owner
        );
        Assert.Equal("financial.portal_import_batch.submitted", staff.Kind);
        var ownerValidation = Assert.Single(resolver.Calls, c => c.Audience is TenantOwnerAudience).Validation;
        Assert.Equal(FinancialInsightsPermissions.View, ownerValidation.RequiredCapability);
        Assert.Equal(Module, ownerValidation.RequiredModule);
    }

    [Fact]
    public async Task Portal_batch_does_not_notify_a_client_that_is_not_the_active_portal_user()
    {
        var harness = new NotificationDispatcherHarness();
        var assigned = Guid.NewGuid();

        await Handle(
            harness,
            new Resolver { Allowed = [assigned], PortalUser = Guid.NewGuid() },
            new Assignments(assigned),
            Batch(FinancialImportBatchSources.Portal, completed: 1)
        );

        var evt = Single(harness);
        Assert.Equal(assigned, evt.RecipientUserId);
    }

    [Fact]
    public void Catalog_declares_the_three_kinds_on_the_financial_import_batch_resource()
    {
        foreach (
            var kind in new[]
            {
                NotificationKinds.FinancialImportBatchCompleted,
                NotificationKinds.FinancialPortalImportBatchCompleted,
                NotificationKinds.FinancialPortalImportBatchSubmitted,
            }
        )
        {
            var entry = NotificationCatalog.GetRequired(kind);
            Assert.Equal("financial_import_batch", NotificationContractNames.For(entry.ResourceType));
            Assert.Contains(NotificationChannel.InApp, entry.AllowedChannels);
            Assert.False(entry.IsMandatory);
        }

        Assert.Equal(
            NotificationRecipientPolicy.CustomerPortal,
            NotificationCatalog.GetRequired(NotificationKinds.FinancialPortalImportBatchCompleted).RecipientPolicy
        );
    }

    private static InAppNotificationRequestedIntegrationEvent Single(NotificationDispatcherHarness harness) =>
        Assert.IsType<InAppNotificationRequestedIntegrationEvent>(Assert.Single(harness.Publisher.Published));

    private static Task Handle(
        NotificationDispatcherHarness harness,
        Resolver resolver,
        Assignments assignments,
        FinancialImportBatchCompletedIntegrationEvent evt
    ) =>
        FinancialImportBatchCompletedConsumer.Handle(
            evt,
            harness.Dispatcher,
            assignments,
            resolver,
            new TestCorrelationContext(),
            CancellationToken.None
        );

    private static FinancialImportBatchCompletedIntegrationEvent Batch(
        string source,
        int completed = 0,
        int needsReview = 0,
        int failed = 0,
        int rejected = 0
    ) =>
        new()
        {
            TenantId = TenantId,
            CorrelationId = "corr-financial",
            BatchId = BatchId,
            CustomerId = CustomerId,
            CreatedByUserId = Creator,
            Source = source,
            CompletedDocuments = completed,
            NeedsReviewDocuments = needsReview,
            FailedDocuments = failed,
            RejectedDocuments = rejected,
            CompletedAtUtc = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc),
        };

    private sealed class Assignments(params Guid[] users) : ICustomerAssignmentAudienceRepository
    {
        public Task<IReadOnlyCollection<Guid>> GetAssignedUserIdsAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyCollection<Guid>>(users);
    }

    // Participants devuelve solo los candidatos que pasan la validación simulada.
    private sealed class Resolver : IRecipientResolver
    {
        public Guid[] Allowed { get; init; } = [];
        public Guid? PortalUser { get; init; }
        public Guid? Owner { get; init; }
        public bool ModuleDisabled { get; init; }
        public List<(NotificationAudience Audience, RecipientValidation Validation)> Calls { get; } = [];

        public Task<IReadOnlyList<Guid>> ResolveAsync(ByPermission audience, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<Result<IReadOnlyList<Guid>>> ResolveAsync(
            NotificationAudience audience,
            RecipientValidation validation,
            CancellationToken ct = default
        )
        {
            Calls.Add((audience, validation));
            if (ModuleDisabled)
                return Fail("Notification.Audience.ModuleDisabled");

            return audience switch
            {
                Participants p => Ok(p.UserIds.Where(Allowed.Contains).ToArray()),
                CustomerPortal => PortalUser is { } portal
                    ? Ok([portal])
                    : Fail("Notification.Audience.NoPortalAccount"),
                TenantOwnerAudience => Owner is { } owner ? Ok([owner]) : Fail("Notification.Audience.NoOwner"),
                _ => Fail("Notification.Audience.Unsupported"),
            };
        }

        private static Task<Result<IReadOnlyList<Guid>>> Ok(Guid[] ids) =>
            Task.FromResult(Result.Success<IReadOnlyList<Guid>>(ids));

        private static Task<Result<IReadOnlyList<Guid>>> Fail(string code) =>
            Task.FromResult(Result.Failure<IReadOnlyList<Guid>>(new Error(code, code)));
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
