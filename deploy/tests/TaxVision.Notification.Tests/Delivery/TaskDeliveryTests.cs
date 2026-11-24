using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Domain.Directory;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests.Delivery;

public sealed class TaskAssignedConsumerDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = DeliveryCase.Recorder();

        await TaskAssignedConsumer.Handle(
            new TaskAssignedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                TaskId = DeliveryCase.ResourceId,
                Title = "File the 1040",
                AssigneeUserId = DeliveryCase.StaffUserId,
                DueOn = new DateOnly(2026, 12, 1),
                DueAtUtc = new DateTime(2026, 12, 2, 4, 59, 59, DateTimeKind.Utc),
            },
            recorder.Dispatcher,
            recorder.Correlation,
            CancellationToken.None
        );

        DeliveryCase.Same(
            [
                "inapp kind=tasks.task_assigned to=staff cat=Work priority=Normal title=You were assigned a task: File the 1040 body=Due Dec 1, 2026. rt=task rid=resource ctx= params=taskTitle=File the 1040 action=open src=evt srcType=tasks.task_assigned.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log InApp kind=tasks.task_assigned tpl=tasks.task_assigned to=user:staff status=Sent event=evt subject=You were assigned a task: File the 1040",
            ],
            recorder.Lines()
        );
    }
}

public sealed class TaskUnblockedConsumerDeliveryTests
{
    [Fact]
    public async Task The_assignee_hears_that_the_task_can_start()
    {
        var recorder = DeliveryCase.Recorder();

        await TaskUnblockedConsumer.Handle(
            Unblocked(DeliveryCase.StaffUserId),
            recorder.Dispatcher,
            recorder.Correlation,
            CancellationToken.None
        );

        DeliveryCase.Same(
            [
                "inapp kind=tasks.task_unblocked to=staff cat=Work priority=Normal title=Task ready to start: File the 1040 body=Everything it was waiting on is done. rt=task rid=resource ctx= params=taskTitle=File the 1040 action=open src=evt srcType=tasks.task_unblocked.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log InApp kind=tasks.task_unblocked tpl=tasks.task_unblocked to=user:staff status=Sent event=evt subject=Task ready to start: File the 1040",
            ],
            recorder.Lines()
        );
    }

    [Fact]
    public async Task Without_an_assignee_nobody_is_notified()
    {
        var recorder = DeliveryCase.Recorder();

        await TaskUnblockedConsumer.Handle(
            Unblocked(null),
            recorder.Dispatcher,
            recorder.Correlation,
            CancellationToken.None
        );

        Assert.Empty(recorder.Lines());
    }

    private static TaskUnblockedIntegrationEvent Unblocked(Guid? assignee) =>
        new()
        {
            EventId = DeliveryCase.EventId,
            TenantId = DeliveryCase.TenantId,
            OccurredOn = DeliveryCase.OccurredOn,
            CorrelationId = "corr",
            TaskId = DeliveryCase.ResourceId,
            Title = "File the 1040",
            AssigneeUserId = assignee,
        };
}

public sealed class TaskOverdueConsumerDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = DeliveryCase.Recorder();

        await TaskOverdueConsumer.Handle(
            new TaskOverdueIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                TaskId = DeliveryCase.ResourceId,
                Title = "File the 1040",
                DueOn = new DateOnly(2026, 11, 19),
                DueAtUtc = new DateTime(2026, 11, 20, 4, 59, 59, DateTimeKind.Utc),
                IsStatutory = true,
                AssigneeUserId = DeliveryCase.StaffUserId,
                CustomerId = DeliveryCase.CustomerId,
            },
            recorder.Dispatcher,
            recorder.Correlation,
            CancellationToken.None
        );

        DeliveryCase.Same(
            [
                "inapp kind=tasks.task_overdue to=staff cat=Work priority=High title=Statutory deadline passed: File the 1040 body=It was due Nov 19, 2026 and is still open. rt=task rid=resource ctx=customerId=customer params=dueAtUtc=2026-11-20T04:59:59.0000000Z;dueOn=2026-11-19;taskTitle=File the 1040 action=open src=evt srcType=tasks.task_overdue.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log InApp kind=tasks.task_overdue tpl=task.overdue to=user:staff status=Sent event=evt subject=Statutory deadline passed: File the 1040",
                "log Push kind=tasks.task_overdue tpl=tasks.task_overdue to=staff status=Sent event=evt subject=Task overdue",
                "push token=token-staff title=Task overdue body=A task assigned to you is past its due date. link=https://office.example.com/n/open?rt=task&rid=resource&cid=customer&ev=evt tag= ttl= data=customerId=customer;kind=tasks.task_overdue;link=https://office.example.com/n/open?rt=task&rid=resource&cid=customer&ev=evt;recipientUserId=staff;resourceId=resource;resourceType=task;sourceEventId=evt;tenantId=tenant",
            ],
            recorder.Lines()
        );
    }
}

public sealed class TaskAttachmentRejectedConsumerDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = DeliveryCase.Recorder();

        await TaskAttachmentRejectedConsumer.Handle(
            new TaskAttachmentRejectedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                TaskId = DeliveryCase.ResourceId,
                TaskTitle = "File the 1040",
                AttachmentId = Guid.NewGuid(),
                FileId = Guid.NewGuid(),
                DisplayName = "w2.pdf",
                Reason = "malware",
                AttachedByUserId = DeliveryCase.StaffUserId,
            },
            recorder.Dispatcher,
            recorder.Correlation,
            CancellationToken.None
        );

        DeliveryCase.Same(
            [
                "inapp kind=tasks.attachment_rejected to=staff cat=Work priority=High title=Task attachment rejected body=Review the rejected attachment and the requested changes. rt=task rid=resource ctx= params=displayName=w2.pdf;reason=malware;taskTitle=File the 1040 action=review src=evt srcType=tasks.attachment_rejected.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log InApp kind=tasks.attachment_rejected tpl=task.attachment_rejected to=user:staff status=Sent event=evt subject=Task attachment rejected",
                "log Push kind=tasks.attachment_rejected tpl=tasks.attachment_rejected to=staff status=Sent event=evt subject=File rejected",
                "push token=token-staff title=File rejected body=A file on one of your tasks did not pass the security scan. link=https://office.example.com/n/open?rt=task&rid=resource&ev=evt tag= ttl= data=kind=tasks.attachment_rejected;link=https://office.example.com/n/open?rt=task&rid=resource&ev=evt;recipientUserId=staff;resourceId=resource;resourceType=task;sourceEventId=evt;tenantId=tenant",
            ],
            recorder.Lines()
        );
    }
}

public sealed class TaskWaitingOnClientConsumerDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = DeliveryCase.Recorder();

        await TaskWaitingOnClientConsumer.Handle(
            new TaskWaitingOnClientIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                TaskId = DeliveryCase.ResourceId,
                Title = "Send your W-2",
                CustomerId = DeliveryCase.CustomerId,
                ExpectedItems = "W-2",
                RequestedByUserId = DeliveryCase.StaffUserId,
                RequestedAtUtc = DeliveryCase.OccurredOn,
                TaxYear = 2025,
                ClientDueOn = new DateOnly(2026, 12, 1),
                ClientDueAtUtc = new DateTime(2026, 12, 2, 4, 59, 59, DateTimeKind.Utc),
            },
            new DeliveryCase.OneCustomer(),
            AllowAllContactPolicyRepository.Instance,
            recorder.Dispatcher,
            new DeliveryCase.PortalResolver(),
            DeliveryCase.Portal,
            DeliveryCase.Host,
            recorder.Correlation,
            CancellationToken.None
        );

        DeliveryCase.Same(
            [
                "email to=client@example.com tpl=task.waiting_on_client.v1 event=evt scope=System confidential=False idem= attachments=0",
                "event NotificationsEmailSendRequestedIntegrationEvent To=client@example.com TemplateKey=task.waiting_on_client.v1",
                "inapp kind=tasks.waiting_on_client to=portal cat=Portal priority=Normal title=Task waiting on you body=Your office needs information to continue this task. rt=task rid=resource ctx=customerId=customer params=title=Send your W-2 action=open src=evt srcType=tasks.waiting_on_client.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log Email kind=task.waiting_on_client.v1 tpl=task.waiting_on_client.v1 to=client@example.com status=Pending event=evt subject=subject",
                "log InApp kind=tasks.waiting_on_client tpl=tasks.waiting_on_client to=user:portal status=Sent event=evt subject=Task waiting on you",
                "render task.waiting_on_client.v1 audience=Client vars=client_due_at_utc={\"$date\":\"2026-12-01\"};customer_name=Client Name;expected_items=W-2;language=En;portal_link=https://office.example.com/portal;task_title=Send your W-2;tax_year=2025",
            ],
            recorder.Lines()
        );
    }
}

public sealed class ClientRequestCreatedConsumerDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = DeliveryCase.Recorder();

        await ClientRequestCreatedConsumer.Handle(
            new ClientRequestCreatedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                ClientRequestId = DeliveryCase.ResourceId,
                CustomerId = DeliveryCase.CustomerId,
                Title = "Upload your 1099",
                Details = "Both pages",
                DueOn = new DateOnly(2026, 12, 1),
                DueAtUtc = new DateTime(2026, 12, 2, 4, 59, 59, DateTimeKind.Utc),
                RequestedByUserId = DeliveryCase.StaffUserId,
            },
            new DeliveryCase.OneCustomer(),
            AllowAllContactPolicyRepository.Instance,
            recorder.Dispatcher,
            new DeliveryCase.PortalResolver(),
            DeliveryCase.Portal,
            DeliveryCase.Host,
            recorder.Correlation,
            CancellationToken.None
        );

        DeliveryCase.Same(
            [
                "email to=client@example.com tpl=task.client_request_created.v1 event=evt scope=System confidential=False idem= attachments=0",
                "event NotificationsEmailSendRequestedIntegrationEvent To=client@example.com TemplateKey=task.client_request_created.v1",
                "inapp kind=tasks.client_request_created to=portal cat=Portal priority=High title=New request from your office body=Open the request to see what is needed. rt=client_request rid=resource ctx=customerId=customer params=title=Upload your 1099 action=open src=evt srcType=tasks.client_request_created.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log Email kind=task.client_request_created.v1 tpl=task.client_request_created.v1 to=client@example.com status=Pending event=evt subject=subject",
                "log InApp kind=tasks.client_request_created tpl=task.client_request_created to=user:portal status=Sent event=evt subject=New request from your office",
                "render task.client_request_created.v1 audience=Client vars=customer_name=Client Name;due_at_utc={\"$date\":\"2026-12-01\"};language=En;portal_link=https://office.example.com/portal/client/documents;request_details=Both pages;request_title=Upload your 1099",
            ],
            recorder.Lines()
        );
    }
}

public sealed class ClientRequestFulfilledConsumerDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = DeliveryCase.Recorder();

        await ClientRequestFulfilledConsumer.Handle(
            new ClientRequestFulfilledIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                ClientRequestId = DeliveryCase.ResourceId,
                CustomerId = DeliveryCase.CustomerId,
                Title = "Upload your 1099",
                RequestedByUserId = Guid.NewGuid(),
                StaffRecipientUserId = DeliveryCase.StaffUserId,
                DocumentCount = 2,
            },
            recorder.Dispatcher,
            recorder.Correlation,
            CancellationToken.None
        );

        DeliveryCase.Same(
            [
                "inapp kind=tasks.client_request_fulfilled to=staff cat=Work priority=Normal title=Client request fulfilled body=The requested items are ready for review. rt=client_request rid=resource ctx=customerId=customer params=documentCount=2;title=Upload your 1099 action=review src=evt srcType=tasks.client_request_fulfilled.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log InApp kind=tasks.client_request_fulfilled tpl=task.client_request_fulfilled to=user:staff status=Sent event=evt subject=Client request fulfilled",
                "log Push kind=tasks.client_request_fulfilled tpl=tasks.client_request_fulfilled to=staff status=Sent event=evt subject=Client request fulfilled",
                "push token=token-staff title=Client request fulfilled body=A client uploaded the documents you asked for. link=https://office.example.com/n/open?rt=client_request&rid=resource&cid=customer&ev=evt tag= ttl= data=customerId=customer;kind=tasks.client_request_fulfilled;link=https://office.example.com/n/open?rt=client_request&rid=resource&cid=customer&ev=evt;recipientUserId=staff;resourceId=resource;resourceType=client_request;sourceEventId=evt;tenantId=tenant",
            ],
            recorder.Lines()
        );
    }
}

public sealed class ClientRequestDocumentRejectedConsumerDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = DeliveryCase.Recorder();

        await ClientRequestDocumentRejectedConsumer.Handle(
            new ClientRequestDocumentRejectedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                ClientRequestId = DeliveryCase.ResourceId,
                CustomerId = DeliveryCase.CustomerId,
                FileId = Guid.NewGuid(),
                DisplayName = "w2.pdf",
                Reason = "malware",
                ClientMessage = "We couldn't process your file.",
                RequestedByUserId = Guid.NewGuid(),
                StaffRecipientUserId = DeliveryCase.StaffUserId,
            },
            new DeliveryCase.OneCustomer(),
            AllowAllContactPolicyRepository.Instance,
            recorder.Dispatcher,
            new DeliveryCase.PortalResolver(),
            DeliveryCase.Portal,
            DeliveryCase.Host,
            recorder.Locales,
            recorder.Correlation,
            CancellationToken.None
        );

        DeliveryCase.Same(
            [
                "email to=client@example.com tpl=task.client_request_document_rejected.v1 event=evt scope=System confidential=False idem= attachments=0",
                "event NotificationsEmailSendRequestedIntegrationEvent To=client@example.com TemplateKey=task.client_request_document_rejected.v1",
                "inapp kind=tasks.client_request_document_rejected to=portal cat=Portal priority=High title=Document needs changes body=Your office rejected a document in this request. rt=client_request rid=resource ctx=customerId=customer params=displayName=w2.pdf action=review src=evt srcType=tasks.client_request_document_rejected.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "inapp kind=tasks.client_request_document_rejected to=staff cat=Portal priority=High title=Document needs changes body=Your office rejected a document in this request. rt=client_request rid=resource ctx=customerId=customer params=displayName=w2.pdf;reason=malware action=review src=evt srcType=tasks.client_request_document_rejected.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log Email kind=task.client_request_document_rejected.v1 tpl=task.client_request_document_rejected.v1 to=client@example.com status=Pending event=evt subject=subject",
                "log InApp kind=tasks.client_request_document_rejected tpl=task.client_request_document_rejected to=user:staff status=Sent event=evt subject=Document needs changes",
                "log InApp kind=tasks.client_request_document_rejected tpl=tasks.client_request_document_rejected to=user:portal status=Sent event=evt subject=Document needs changes",
                "render task.client_request_document_rejected.v1 audience=Client vars=client_message=We couldn't process this file. Please upload it again.;customer_name=Client Name;document_name=w2.pdf;language=En;portal_link=https://office.example.com/portal/client/documents;request_title=w2.pdf",
            ],
            recorder.Lines()
        );
    }
}

public sealed class TaskSeriesEndedConsumerDeliveryTests
{
    [Fact]
    public async Task The_owner_hears_that_the_recurring_task_ended()
    {
        var recorder = DeliveryCase.Recorder();

        await TaskSeriesEndedConsumer.Handle(
            new TaskSeriesEndedIntegrationEvent
            {
                EventId = DeliveryCase.EventId,
                TenantId = DeliveryCase.TenantId,
                OccurredOn = DeliveryCase.OccurredOn,
                CorrelationId = "corr",
                SeriesId = DeliveryCase.ResourceId,
                Title = "Payroll (941)",
                OwnerUserId = DeliveryCase.StaffUserId,
            },
            recorder.Dispatcher,
            recorder.Correlation,
            CancellationToken.None
        );

        Assert.Contains(
            recorder.Lines(),
            line =>
                line.StartsWith("inapp kind=tasks.series_ended to=staff", StringComparison.Ordinal)
                && line.Contains("title=Recurring task ended: Payroll (941)", StringComparison.Ordinal)
        );
    }
}

/// <summary>Otro movió el vencimiento de mi tarea: me entero; si lo moví yo, no.</summary>
public sealed class TaskDueChangedConsumerDeliveryTests
{
    [Fact]
    public async Task The_assignee_hears_when_someone_else_moves_the_due_date()
    {
        var recorder = DeliveryCase.Recorder();

        await TaskDueChangedConsumer.Handle(
            DueChanged(changedBy: Guid.NewGuid()),
            recorder.Dispatcher,
            recorder.Correlation,
            CancellationToken.None
        );

        DeliveryCase.Same(
            [
                "inapp kind=tasks.task_due_changed to=staff cat=Work priority=Normal title=Due date changed: File the 1040 body=New due date: Apr 15, 2027. rt=task rid=resource ctx= params=taskTitle=File the 1040 action=open src=evt srcType=tasks.task_due_changed.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log InApp kind=tasks.task_due_changed tpl=tasks.task_due_changed to=user:staff status=Sent event=evt subject=Due date changed: File the 1040",
            ],
            recorder.Lines()
        );
    }

    [Fact]
    public async Task Moving_my_own_due_date_tells_me_nothing()
    {
        var recorder = DeliveryCase.Recorder();

        await TaskDueChangedConsumer.Handle(
            DueChanged(changedBy: DeliveryCase.StaffUserId),
            recorder.Dispatcher,
            recorder.Correlation,
            CancellationToken.None
        );

        Assert.Empty(recorder.Lines());
    }

    private static TaskDueChangedIntegrationEvent DueChanged(Guid changedBy) =>
        new()
        {
            EventId = DeliveryCase.EventId,
            TenantId = DeliveryCase.TenantId,
            OccurredOn = DeliveryCase.OccurredOn,
            CorrelationId = "corr",
            TaskId = DeliveryCase.ResourceId,
            Title = "File the 1040",
            AssigneeUserId = DeliveryCase.StaffUserId,
            NewDueOn = new DateOnly(2027, 4, 15),
            NewDueAtUtc = new DateTime(2027, 4, 16, 3, 59, 59, DateTimeKind.Utc),
            TimeZoneId = "America/New_York",
            ChangedByUserId = changedBy,
        };
}
