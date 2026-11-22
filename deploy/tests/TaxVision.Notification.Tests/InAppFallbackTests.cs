using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Messaging.NotificationIntegrationEvents;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Application.Consumers.Communication;
using TaxVision.Notification.Tests.Lifecycle;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

// El respaldo del in-app sale del archivo de textos en inglés, con los parámetros puestos (un consumer por grupo).
public sealed class InAppFallbackTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    public sealed class TasksAndRequests
    {
        [Fact]
        public async Task Fallback_comes_from_the_copy_catalog()
        {
            var harness = new NotificationDispatcherHarness();

            await TaskAttachmentRejectedConsumer.Handle(
                new TaskAttachmentRejectedIntegrationEvent
                {
                    TenantId = TenantId,
                    TaskId = Guid.NewGuid(),
                    TaskTitle = "File the 1040",
                    AttachmentId = Guid.NewGuid(),
                    FileId = Guid.NewGuid(),
                    DisplayName = "w2.pdf",
                    Reason = "malware",
                    AttachedByUserId = UserId,
                },
                harness.Dispatcher,
                new NoOpCorrelation(),
                default
            );

            var inApp = Single(harness);
            Assert.Equal("Task attachment rejected", inApp.FallbackTitle);
            Assert.Equal("Review the rejected attachment and the requested changes.", inApp.FallbackBody);
            Assert.Equal("malware", inApp.Params!["reason"]);
        }
    }

    public sealed class StorageAndLegal
    {
        [Fact]
        public async Task Fallback_comes_from_the_copy_catalog()
        {
            var harness = new NotificationDispatcherHarness();

            await ShareLinkFolderItemAddedConsumer.Handle(
                new ShareLinkFolderItemAddedIntegrationEvent
                {
                    TenantId = TenantId,
                    ShareLinkId = Guid.NewGuid(),
                    FolderId = Guid.NewGuid(),
                    FileId = Guid.NewGuid(),
                    AutoCovered = true,
                    CreatedByUserId = UserId,
                },
                harness.Dispatcher,
                AlwaysOnInAppSource.Instance,
                new NoOpCorrelation(),
                default
            );

            var inApp = Single(harness);
            Assert.Equal("New item in a shared folder", inApp.FallbackTitle);
            Assert.Equal("A new item was added to a folder you shared.", inApp.FallbackBody);
            Assert.Equal("true", inApp.Params!["autoCovered"]);
        }
    }

    public sealed class RecordingsAndConnectors
    {
        [Fact]
        public async Task Fallback_comes_from_the_copy_catalog()
        {
            var harness = new NotificationDispatcherHarness();

            await CallRecordingFailedConsumer.Handle(
                new CallRecordingFailedIntegrationEvent
                {
                    TenantId = TenantId,
                    CallId = Guid.NewGuid(),
                    Reason = "ConsentTimeout",
                    FailedAtUtc = DateTime.UtcNow,
                    CallerUserId = UserId,
                    CalleeUserId = Guid.NewGuid(),
                },
                harness.Dispatcher,
                new NoOpCorrelation(),
                default
            );

            var inApp = harness.Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>().First();
            Assert.Equal("Call recording failed", inApp.FallbackTitle);
            Assert.Equal("The call recording could not be processed.", inApp.FallbackBody);
            Assert.Equal("ConsentTimeout", inApp.Params!["reason"]);
        }
    }

    public sealed class Imports
    {
        [Fact]
        public async Task Fallback_comes_from_the_copy_catalog()
        {
            var harness = new NotificationDispatcherHarness();

            await CustomerImportFailedConsumer.Handle(
                new CustomerImportFailedIntegrationEvent
                {
                    TenantId = TenantId,
                    ImportJobId = Guid.NewGuid(),
                    CreatedByUserId = UserId,
                    Reason = "File is empty.",
                    FailedAtUtc = DateTime.UtcNow,
                },
                harness.Dispatcher,
                new NoOpCorrelation(),
                default
            );

            var inApp = Single(harness);
            Assert.Equal("Client import failed", inApp.FallbackTitle);
            Assert.Equal("Review the import errors and try again.", inApp.FallbackBody);
        }
    }

    private static InAppNotificationRequestedIntegrationEvent Single(NotificationDispatcherHarness harness) =>
        Assert.Single(harness.Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>());
}
