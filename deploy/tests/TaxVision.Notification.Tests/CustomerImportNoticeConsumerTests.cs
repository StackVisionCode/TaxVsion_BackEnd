using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Messaging.NotificationIntegrationEvents;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Tests.Lifecycle;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>
/// Notification es el único dueño del aviso de import terminado o fallido: avisa siempre, sin
/// depender de ninguna variable (Communication ya no lo crea).
/// </summary>
public sealed class CustomerImportNoticeConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CreatedBy = Guid.NewGuid();
    private static readonly Guid Successor = Guid.NewGuid();

    [Fact]
    public async Task Completed_import_notifies_its_creator_without_any_setting()
    {
        var harness = new NotificationDispatcherHarness();

        await CustomersBulkImportedConsumer.Handle(
            Completed(notifyUser: null),
            harness.Dispatcher,
            new NoOpCorrelation(),
            CancellationToken.None
        );

        var inApp = Assert.Single(harness.Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>());
        Assert.Equal(CreatedBy, inApp.RecipientUserId);
        Assert.Equal(NotificationKinds.CustomerBulkImported, inApp.Kind);
    }

    [Fact]
    public async Task Completed_import_notifies_the_successor_when_customer_names_one()
    {
        var harness = new NotificationDispatcherHarness();

        await CustomersBulkImportedConsumer.Handle(
            Completed(notifyUser: Successor),
            harness.Dispatcher,
            new NoOpCorrelation(),
            CancellationToken.None
        );

        var inApp = Assert.Single(harness.Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>());
        Assert.Equal(Successor, inApp.RecipientUserId);
    }

    // El CRM arma el texto con los conteos: creados, actualizados, saltados y con errores.
    [Fact]
    public async Task Completed_import_sends_every_count()
    {
        var harness = new NotificationDispatcherHarness();

        await CustomersBulkImportedConsumer.Handle(
            Completed(notifyUser: null) with
            {
                UpdatedCount = 4,
                SkippedCount = 5,
            },
            harness.Dispatcher,
            new NoOpCorrelation(),
            CancellationToken.None
        );

        var inApp = Assert.Single(harness.Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>());
        Assert.Equal("2", inApp.Params["successCount"]);
        Assert.Equal("4", inApp.Params["updatedCount"]);
        Assert.Equal("5", inApp.Params["skippedCount"]);
        Assert.Equal("1", inApp.Params["failedCount"]);
    }

    [Fact]
    public async Task Failed_import_notifies_its_creator_without_any_setting()
    {
        var harness = new NotificationDispatcherHarness();

        await CustomerImportFailedConsumer.Handle(
            new CustomerImportFailedIntegrationEvent
            {
                TenantId = TenantId,
                CorrelationId = "corr",
                ImportJobId = Guid.NewGuid(),
                CreatedByUserId = CreatedBy,
                Reason = "File is empty.",
                FailedAtUtc = DateTime.UtcNow,
            },
            harness.Dispatcher,
            new NoOpCorrelation(),
            CancellationToken.None
        );

        var inApp = Assert.Single(harness.Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>());
        Assert.Equal(CreatedBy, inApp.RecipientUserId);
        Assert.Equal(NotificationKinds.CustomerImportFailed, inApp.Kind);
    }

    private static CustomersBulkImportedIntegrationEvent Completed(Guid? notifyUser) =>
        new()
        {
            TenantId = TenantId,
            CorrelationId = "corr",
            ImportJobId = Guid.NewGuid(),
            CreatedByUserId = CreatedBy,
            NotifyUserId = notifyUser,
            CompletedAtUtc = DateTime.UtcNow,
            TotalRows = 3,
            SuccessCount = 2,
            UpdatedCount = 0,
            SkippedCount = 0,
            FailedCount = 1,
            CreatedCustomerIds = [],
            UpdatedCustomerIds = [],
        };
}
