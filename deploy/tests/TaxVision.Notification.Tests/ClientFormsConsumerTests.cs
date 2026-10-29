using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers.ClientForms;
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Directory;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

public sealed class ClientFormsConsumerTests
{
    [Fact]
    public async Task Submitted_notifies_only_staff_with_review_and_customer_access()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var assignedReviewer = Guid.NewGuid();
        var requesterWithoutAccess = Guid.NewGuid();
        var responsibleWithoutReview = Guid.NewGuid();
        var harness = new NotificationDispatcherHarness();
        var resolver = new Resolver()
            .Allow(BuildingBlocks.Authorization.TasksPermissions.FormRequestsReview, assignedReviewer)
            .Allow(BuildingBlocks.Authorization.CustomersPermissions.ViewAll);

        await FormRequestSubmittedConsumer.Handle(
            Submitted(tenantId, customerId, requesterWithoutAccess, responsibleWithoutReview),
            Sender(harness),
            new Assignments(assignedReviewer),
            resolver,
            new Correlation(),
            CancellationToken.None
        );

        var notification = Assert.Single(harness.Publisher.Published);
        Assert.Equal(
            assignedReviewer,
            Assert
                .IsType<BuildingBlocks.Messaging.NotificationIntegrationEvents.InAppNotificationRequestedIntegrationEvent>(
                    notification
                )
                .RecipientUserId
        );
    }

    [Fact]
    public async Task Submitted_includes_requester_with_view_all_and_review()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var requester = Guid.NewGuid();
        var harness = new NotificationDispatcherHarness();
        var resolver = new Resolver()
            .Allow(BuildingBlocks.Authorization.CustomersPermissions.ViewAll, requester)
            .Allow(BuildingBlocks.Authorization.TasksPermissions.FormRequestsReview, requester);

        await FormRequestSubmittedConsumer.Handle(
            Submitted(tenantId, customerId, requester, responsibleUserId: null),
            Sender(harness),
            new Assignments(),
            resolver,
            new Correlation(),
            CancellationToken.None
        );

        var notification = Assert.Single(harness.Publisher.Published);
        Assert.Equal(
            requester,
            Assert
                .IsType<BuildingBlocks.Messaging.NotificationIntegrationEvents.InAppNotificationRequestedIntegrationEvent>(
                    notification
                )
                .RecipientUserId
        );
    }

    [Fact]
    public async Task Submitted_excludes_responsible_with_review_but_without_customer_access()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var assignedReviewer = Guid.NewGuid();
        var responsibleWithoutAccess = Guid.NewGuid();
        var harness = new NotificationDispatcherHarness();
        var resolver = new Resolver().Allow(
            BuildingBlocks.Authorization.TasksPermissions.FormRequestsReview,
            assignedReviewer,
            responsibleWithoutAccess
        );

        await FormRequestSubmittedConsumer.Handle(
            Submitted(tenantId, customerId, assignedReviewer, responsibleWithoutAccess),
            Sender(harness),
            new Assignments(assignedReviewer),
            resolver,
            new Correlation(),
            CancellationToken.None
        );

        var notification = Assert.Single(harness.Publisher.Published);
        Assert.Equal(
            assignedReviewer,
            Assert
                .IsType<BuildingBlocks.Messaging.NotificationIntegrationEvents.InAppNotificationRequestedIntegrationEvent>(
                    notification
                )
                .RecipientUserId
        );
    }

    [Fact]
    public async Task Submitted_includes_responsible_with_view_all_and_review()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var requesterWithoutAccess = Guid.NewGuid();
        var responsible = Guid.NewGuid();
        var harness = new NotificationDispatcherHarness();
        var resolver = new Resolver()
            .Allow(BuildingBlocks.Authorization.CustomersPermissions.ViewAll, responsible)
            .Allow(
                BuildingBlocks.Authorization.TasksPermissions.FormRequestsReview,
                responsible,
                requesterWithoutAccess
            );

        await FormRequestSubmittedConsumer.Handle(
            Submitted(tenantId, customerId, requesterWithoutAccess, responsible),
            Sender(harness),
            new Assignments(),
            resolver,
            new Correlation(),
            CancellationToken.None
        );

        var notification = Assert.Single(harness.Publisher.Published);
        Assert.Equal(
            responsible,
            Assert
                .IsType<BuildingBlocks.Messaging.NotificationIntegrationEvents.InAppNotificationRequestedIntegrationEvent>(
                    notification
                )
                .RecipientUserId
        );
    }

    [Fact]
    public async Task Cancelled_does_not_notify_when_client_notification_is_disabled()
    {
        var harness = new NotificationDispatcherHarness();

        await FormRequestCancelledConsumer.Handle(
            new FormRequestCancelledIntegrationEvent
            {
                TenantId = Guid.NewGuid(),
                CorrelationId = "corr-cancelled",
                FormRequestId = Guid.NewGuid(),
                Participant = CustomerParticipant(Guid.NewGuid()),
                Title = "Tax organizer",
                Reason = "staff_cancelled",
                NotifyClient = false,
            },
            Sender(harness),
            new Resolver(),
            new Correlation(),
            CancellationToken.None
        );

        Assert.Empty(harness.Publisher.Published);
    }

    [Fact]
    public async Task Assigned_respects_email_opt_out_and_missing_portal_account()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var render = new RenderClient();
        var gateway = new EmailGateway();
        var harness = new NotificationDispatcherHarness();
        var customers = new CustomerDirectory(
            CustomerEmailDirectoryEntry.Create(tenantId, customerId, "client@example.com", "Client")
        );
        var policies = new ContactPolicies(
            CustomerContactPolicyProjection.Create(tenantId, customerId, emailEnabled: false, smsEnabled: true)
        );

        await FormRequestAssignedConsumer.Handle(
            new FormRequestAssignedIntegrationEvent
            {
                TenantId = tenantId,
                CorrelationId = "corr-assigned",
                FormRequestId = Guid.NewGuid(),
                Participant = CustomerParticipant(customerId),
                Title = "Tax organizer",
                HasMessage = true,
                RequestedByUserId = Guid.NewGuid(),
                FormTemplateId = Guid.NewGuid(),
                VersionNumber = 1,
            },
            Sender(harness, render, gateway, customers, policies),
            new Resolver(),
            new Correlation(),
            CancellationToken.None
        );

        Assert.Equal(0, render.Calls);
        Assert.Empty(gateway.Queued);
        Assert.Empty(harness.Publisher.Published);
    }

    [Fact]
    public async Task Optional_email_respects_the_user_preference()
    {
        var harness = new NotificationDispatcherHarness();
        var render = new RenderClient();
        var gateway = new EmailGateway();
        var sender = Sender(
            harness,
            render,
            gateway,
            userEmails: new UserEmailResolver("staff@example.com"),
            preferences: new Preferences(false)
        );

        await sender.SendAsync(StaffRequest(NotificationKinds.TasksFormRequestSubmitted), CancellationToken.None);

        Assert.Equal(0, render.Calls);
        Assert.Empty(gateway.Queued);
        Assert.Single(harness.Publisher.Published);
    }

    [Fact]
    public async Task Disabled_tenant_push_flag_stops_before_rendering_push_content()
    {
        var render = new RenderClient();
        var sender = Sender(new NotificationDispatcherHarness(), render, settings: new ClientFormSettings(false));

        await sender.SendAsync(StaffRequest(NotificationKinds.TasksFormRequestSubmitted), CancellationToken.None);

        Assert.Equal(0, render.TextCalls);
    }

    [Fact]
    public async Task Push_without_devices_is_omitted_without_failing_the_other_channels()
    {
        var harness = new NotificationDispatcherHarness();
        var render = new RenderClient();
        var sender = Sender(harness, render, settings: new ClientFormSettings(true));

        await sender.SendAsync(StaffRequest(NotificationKinds.TasksFormRequestSubmitted), CancellationToken.None);

        Assert.Equal(0, render.TextCalls);
        Assert.Single(harness.Publisher.Published);
    }

    [Fact]
    public async Task Transient_push_failure_schedules_a_durable_retry_with_the_same_replacement_ids()
    {
        var push = new SequencePushSender(new Error(PushErrorCodes.Unavailable, "temporarily unavailable"));
        var devices = new SinglePushDeviceRepository();
        var logs = new MemoryNotificationLogs();
        var harness = new NotificationDispatcherHarness(push, devices, logs);
        var retries = new RecordingRetryScheduler();
        var sender = Sender(
            harness,
            new RenderClient(),
            settings: new ClientFormSettings(true),
            retryScheduler: retries
        );

        await sender.SendAsync(StaffRequest(NotificationKinds.TasksFormRequestSubmitted), CancellationToken.None);

        var retry = Assert.Single(retries.Scheduled);
        Assert.Equal(TimeSpan.FromMinutes(1), retry.Delay);
        Assert.Equal(1, retry.Request.Attempt);
        await sender.RetryPushAsync(retry.Request, CancellationToken.None);
        Assert.Equal(2, push.Messages.Count);
        Assert.Equal(push.Messages[0].Topic, push.Messages[1].Topic);
        Assert.Equal(push.Messages[0].Tag, push.Messages[1].Tag);
    }

    [Fact]
    public async Task Mandatory_kind_still_respects_push_opt_out_but_keeps_email_and_in_app()
    {
        var push = new CapturingPushSender();
        var preferences = new Preferences(false);
        var harness = new NotificationDispatcherHarness(
            push,
            new SinglePushDeviceRepository(),
            new MemoryNotificationLogs(),
            preferences
        );
        var gateway = new EmailGateway();
        var sender = Sender(
            harness,
            new RenderClient(),
            gateway,
            userEmails: new UserEmailResolver("user@example.com"),
            preferences: preferences,
            settings: new ClientFormSettings(true)
        );

        await sender.SendAsync(StaffRequest(NotificationKinds.TasksFormRequestAssigned), CancellationToken.None);

        Assert.Single(gateway.Queued);
        Assert.Single(harness.Publisher.Published);
        Assert.Empty(push.Messages);
    }

    [Fact]
    public void Push_identifiers_are_stable_url_safe_and_within_the_web_push_limit()
    {
        var method = typeof(ClientFormNotificationSender).GetMethod(
            "PushIdentifiers",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic
        );
        var eventId = Guid.NewGuid();
        var first = Assert.IsType<ValueTuple<string, string>>(method!.Invoke(null, [eventId]));
        var second = Assert.IsType<ValueTuple<string, string>>(method.Invoke(null, [eventId]));

        Assert.Equal(first, second);
        Assert.True(first.Item1.Length <= 32);
        Assert.Matches("^[A-Za-z0-9_-]+$", first.Item1);
        Assert.Matches("^cf-[A-Za-z0-9_-]+$", first.Item2);
    }

    [Fact]
    public void Push_payload_contains_only_opaque_context_and_uses_the_tenant_host()
    {
        var customerId = Guid.NewGuid();
        var request = StaffRequest(NotificationKinds.TasksFormRequestSubmitted) with
        {
            CustomerId = customerId,
            Variables = new Dictionary<string, object?>
            {
                ["form_title"] = "Private organizer",
                ["customer_name"] = "Private client",
                ["email"] = "private@example.com",
                ["token"] = "secret-token",
            },
        };
        var dataMethod = typeof(ClientFormNotificationSender).GetMethod(
            "PushData",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic
        );
        var linkMethod = typeof(ClientFormNotificationSender).GetMethod(
            "PushLink",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        );
        var sender = Sender(new NotificationDispatcherHarness());

        var data = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(dataMethod!.Invoke(null, [request]));
        var link = Assert.IsType<string>(linkMethod!.Invoke(sender, [request, "office.taxproffice.com"]));

        Assert.Equal(["customerId", "kind", "resourceId", "resourceType"], data.Keys.Order().ToArray());
        Assert.Equal("form_request", data["resourceType"]);
        Assert.Equal(request.FormRequestId.ToString(), data["resourceId"]);
        Assert.Equal(customerId.ToString(), data["customerId"]);
        Assert.DoesNotContain(data.Values, value => value.Contains("Private", StringComparison.Ordinal));
        Assert.DoesNotContain(data.Values, value => value.Contains("@", StringComparison.Ordinal));
        Assert.StartsWith("https://office.taxproffice.com/n/open?rt=form_request&rid=", link, StringComparison.Ordinal);
        Assert.Contains($"&cid={customerId:D}", link, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Attachment_rejection_uses_safe_message_without_file_name_or_free_text()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var render = new RenderClient();
        var gateway = new EmailGateway();
        var harness = new NotificationDispatcherHarness();

        await FormAttachmentRejectedConsumer.Handle(
            new FormAttachmentRejectedIntegrationEvent
            {
                TenantId = tenantId,
                CorrelationId = "corr-rejected",
                FormRequestId = Guid.NewGuid(),
                Participant = CustomerParticipant(customerId),
                Title = "Tax organizer",
                AttachmentId = Guid.NewGuid(),
                FileId = Guid.NewGuid(),
                FileExtension = "pdf",
                ReasonCode = "infected",
                RequestedByUserId = Guid.NewGuid(),
            },
            Sender(
                harness,
                render,
                gateway,
                new CustomerDirectory(
                    CustomerEmailDirectoryEntry.Create(tenantId, customerId, "client@example.com", "Client")
                ),
                new ContactPolicies()
            ),
            new Assignments(),
            new Resolver(),
            new Correlation(),
            CancellationToken.None
        );

        Assert.Equal("task.form_attachment_rejected.v1", render.EventKey);
        Assert.DoesNotContain(
            render.Variables.Keys,
            key =>
                key.Equals("file_name", StringComparison.OrdinalIgnoreCase)
                || key.Equals("filename", StringComparison.OrdinalIgnoreCase)
                || key.Equals("original_name", StringComparison.OrdinalIgnoreCase)
        );
        Assert.DoesNotContain(
            render.Variables.Values,
            value => value?.ToString()?.Contains("infected", StringComparison.OrdinalIgnoreCase) == true
        );
        Assert.Equal("pdf", render.Variables["file_extension"]);
        Assert.Contains("security scan", Assert.IsType<string>(render.Variables["client_message"]));
        Assert.Single(gateway.Queued);
    }

    [Fact]
    public async Task Intake_access_code_uses_intake_binding_without_request_or_participant()
    {
        var tenantId = Guid.NewGuid();
        var intakeLinkId = Guid.NewGuid();
        var codec = new AccessCodeCodec(
            new FormAccessCodeDeliveryPayload("prospect@example.com", "123456", "Public intake")
        );
        var gateway = new EmailGateway();
        var render = new RenderClient();

        await FormAccessCodeDeliveryRequestedConsumer.Handle(
            new FormAccessCodeDeliveryRequestedIntegrationEvent
            {
                TenantId = tenantId,
                CorrelationId = "corr-intake-otp",
                CommandId = Guid.NewGuid(),
                IntakeLinkId = intakeLinkId,
                ChallengeId = Guid.NewGuid(),
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10),
                Payload = new ProtectedPayload
                {
                    Cipher = "ciphertext",
                    KeyVersion = 1,
                    Schema = ClientFormProtectedPayloadSchemas.AccessCode,
                },
            },
            codec,
            Sender(new NotificationDispatcherHarness(), render, gateway),
            new Correlation(),
            NullLogger<FormAccessCodeDeliveryRequestedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Equal(intakeLinkId, codec.Binding?.IntakeLinkId);
        Assert.Null(codec.Binding?.FormRequestId);
        Assert.Null(codec.Binding?.Participant);
        Assert.Equal("task.form_access_code.v1", render.EventKey);
        Assert.Equal("123456", render.Variables["code"]);
        Assert.True(Assert.Single(gateway.Queued).Confidential);
    }

    [Fact]
    public async Task Access_code_with_swapped_challenge_is_discarded_with_a_security_warning()
    {
        var tenantId = Guid.NewGuid();
        var intakeLinkId = Guid.NewGuid();
        var codec = RealCodec();
        var payload = codec.Protect(
            ClientFormProtectedPayloadSchemas.AccessCode,
            new ProtectedPayloadBinding(tenantId, null, null, intakeLinkId, Guid.NewGuid()),
            new FormAccessCodeDeliveryPayload("prospect@example.com", "123456", "Public intake")
        );
        var gateway = new EmailGateway();
        var render = new RenderClient();
        var logger = new CapturingLogger<FormAccessCodeDeliveryRequestedIntegrationEvent>();

        await FormAccessCodeDeliveryRequestedConsumer.Handle(
            new FormAccessCodeDeliveryRequestedIntegrationEvent
            {
                TenantId = tenantId,
                CorrelationId = "corr-swapped",
                CommandId = Guid.NewGuid(),
                IntakeLinkId = intakeLinkId,
                ChallengeId = Guid.NewGuid(),
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10),
                Payload = payload,
            },
            codec,
            Sender(new NotificationDispatcherHarness(), render, gateway),
            new Correlation(),
            logger,
            CancellationToken.None
        );

        Assert.Equal(0, render.Calls);
        Assert.Empty(gateway.Queued);
        var warning = Assert.Single(logger.Messages);
        Assert.StartsWith("Security:", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("prospect@example.com", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("123456", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Access_link_bound_to_another_participant_is_discarded_with_a_security_warning()
    {
        var tenantId = Guid.NewGuid();
        var formRequestId = Guid.NewGuid();
        var codec = RealCodec();
        var payload = codec.Protect(
            ClientFormProtectedPayloadSchemas.AccessLink,
            new ProtectedPayloadBinding(tenantId, formRequestId, ExternalParticipant(Guid.NewGuid())),
            new FormAccessLinkDeliveryPayload("guest@example.com", "Guest", "secret-token", "Organizer")
        );
        var gateway = new EmailGateway();
        var render = new RenderClient();
        var logger = new CapturingLogger<FormAccessLinkDeliveryRequestedIntegrationEvent>();

        await FormAccessLinkDeliveryRequestedConsumer.Handle(
            new FormAccessLinkDeliveryRequestedIntegrationEvent
            {
                TenantId = tenantId,
                CorrelationId = "corr-link-swapped",
                CommandId = Guid.NewGuid(),
                FormRequestId = formRequestId,
                Participant = ExternalParticipant(Guid.NewGuid()),
                ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
                Payload = payload,
            },
            codec,
            Sender(new NotificationDispatcherHarness(), render, gateway),
            new HostResolver(),
            Options.Create(new PortalOptions()),
            new Correlation(),
            logger,
            CancellationToken.None
        );

        Assert.Equal(0, render.Calls);
        Assert.Empty(gateway.Queued);
        var warning = Assert.Single(logger.Messages);
        Assert.StartsWith("Security:", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("guest@example.com", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Access_link_email_is_marked_confidential_so_it_skips_the_fanout()
    {
        var tenantId = Guid.NewGuid();
        var formRequestId = Guid.NewGuid();
        var participant = ExternalParticipant(Guid.NewGuid());
        var codec = RealCodec();
        var payload = codec.Protect(
            ClientFormProtectedPayloadSchemas.AccessLink,
            new ProtectedPayloadBinding(tenantId, formRequestId, participant),
            new FormAccessLinkDeliveryPayload("guest@example.com", "Guest", "secret-token", "Organizer")
        );
        var gateway = new EmailGateway();

        await FormAccessLinkDeliveryRequestedConsumer.Handle(
            new FormAccessLinkDeliveryRequestedIntegrationEvent
            {
                TenantId = tenantId,
                CorrelationId = "corr-link",
                CommandId = Guid.NewGuid(),
                FormRequestId = formRequestId,
                Participant = participant,
                ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
                Payload = payload,
            },
            codec,
            Sender(new NotificationDispatcherHarness(), new RenderClient(), gateway),
            new HostResolver(),
            Options.Create(new PortalOptions()),
            new Correlation(),
            NullLogger<FormAccessLinkDeliveryRequestedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        var queued = Assert.Single(gateway.Queued);
        Assert.True(queued.Confidential);
        Assert.Equal("guest@example.com", queued.To);
    }

    [Theory]
    [InlineData(FormParticipantNotificationKinds.Returned, "task.form_participant_returned.v1")]
    [InlineData(FormParticipantNotificationKinds.Reminder, "task.form_participant_reminder.v1")]
    [InlineData(FormParticipantNotificationKinds.Completed, "task.form_participant_completed.v1")]
    [InlineData(FormParticipantNotificationKinds.Cancelled, "task.form_participant_cancelled.v1")]
    [InlineData(FormParticipantNotificationKinds.AttachmentRejected, "task.form_participant_attachment_rejected.v1")]
    public async Task Participant_notice_uses_the_external_template_and_stays_confidential(string kind, string template)
    {
        var codec = RealCodec();
        var command = ParticipantNotice(codec, kind, new Dictionary<string, string>());
        var gateway = new EmailGateway();
        var render = new RenderClient();

        await FormParticipantNotificationRequestedConsumer.Handle(
            command,
            codec,
            Sender(new NotificationDispatcherHarness(), render, gateway),
            new Correlation(),
            NullLogger<FormParticipantNotificationRequestedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Equal(template, render.EventKey);
        Assert.Equal("Intake", render.Variables["form_title"]);
        var queued = Assert.Single(gateway.Queued);
        Assert.Equal("guest@example.com", queued.To);
        Assert.True(queued.Confidential);
        Assert.Equal(command.CommandId.ToString("N"), queued.IdempotencyKey);
    }

    [Fact]
    public async Task Participant_attachment_notice_uses_the_fixed_message_for_the_reason_code()
    {
        var codec = RealCodec();
        var render = new RenderClient();

        await FormParticipantNotificationRequestedConsumer.Handle(
            ParticipantNotice(
                codec,
                FormParticipantNotificationKinds.AttachmentRejected,
                new Dictionary<string, string> { ["file_extension"] = "pdf", ["reason_code"] = "infected" }
            ),
            codec,
            Sender(new NotificationDispatcherHarness(), render, new EmailGateway()),
            new Correlation(),
            NullLogger<FormParticipantNotificationRequestedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Equal("pdf", render.Variables["file_extension"]);
        Assert.Equal(
            "The file couldn't be accepted because our security scan flagged it. Please upload a different copy.",
            render.Variables["client_message"]
        );
    }

    [Fact]
    public async Task Participant_notice_with_an_unknown_kind_is_discarded_without_rendering()
    {
        var codec = RealCodec();
        var gateway = new EmailGateway();
        var render = new RenderClient();

        await FormParticipantNotificationRequestedConsumer.Handle(
            ParticipantNotice(codec, "unsupported", new Dictionary<string, string>()),
            codec,
            Sender(new NotificationDispatcherHarness(), render, gateway),
            new Correlation(),
            NullLogger<FormParticipantNotificationRequestedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Equal(0, render.Calls);
        Assert.Empty(gateway.Queued);
    }

    [Fact]
    public async Task Participant_notice_bound_to_another_request_is_discarded_with_a_security_warning()
    {
        var codec = RealCodec();
        var command = ParticipantNotice(
            codec,
            FormParticipantNotificationKinds.Completed,
            new Dictionary<string, string>()
        ) with
        {
            FormRequestId = Guid.NewGuid(),
        };
        var gateway = new EmailGateway();
        var logger = new CapturingLogger<FormParticipantNotificationRequestedIntegrationEvent>();

        await FormParticipantNotificationRequestedConsumer.Handle(
            command,
            codec,
            Sender(new NotificationDispatcherHarness(), new RenderClient(), gateway),
            new Correlation(),
            logger,
            CancellationToken.None
        );

        Assert.Empty(gateway.Queued);
        var warning = Assert.Single(logger.Messages);
        Assert.StartsWith("Security:", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("guest@example.com", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Customer_push_opens_the_portal_and_staff_push_opens_the_crm()
    {
        var push = new CapturingPushSender();
        var harness = new NotificationDispatcherHarness(
            push,
            new SinglePushDeviceRepository(),
            new MemoryNotificationLogs()
        );
        var sender = Sender(harness, settings: new ClientFormSettings(true));
        var customerId = Guid.NewGuid();

        await sender.SendAsync(
            StaffRequest(NotificationKinds.TasksFormRequestCompleted) with
            {
                CustomerId = customerId,
                CustomerRecipient = true,
            },
            CancellationToken.None
        );
        await sender.SendAsync(StaffRequest(NotificationKinds.TasksFormRequestSubmitted), CancellationToken.None);

        Assert.Equal(2, push.Messages.Count);
        Assert.StartsWith(
            "https://office.taxproffice.com/portal/n/open?rt=form_request&rid=",
            push.Messages[0].Link,
            StringComparison.Ordinal
        );
        Assert.EndsWith($"&cid={customerId:D}", push.Messages[0].Link, StringComparison.Ordinal);
        Assert.StartsWith(
            "https://office.taxproffice.com/n/open?rt=form_request&rid=",
            push.Messages[1].Link,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task Optional_email_reaches_a_client_without_a_portal_account()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var gateway = new EmailGateway();
        var render = new RenderClient();
        var metrics = new RecordingMetrics();
        var sender = Sender(
            new NotificationDispatcherHarness(),
            render,
            gateway,
            new CustomerDirectory(
                CustomerEmailDirectoryEntry.Create(tenantId, customerId, "client@example.com", "Client")
            ),
            new ContactPolicies(CustomerContactPolicyProjection.Create(tenantId, customerId, true, true, "Es")),
            preferences: new Preferences(false),
            metrics: metrics
        );

        await FormRequestCompletedConsumer.Handle(
            new FormRequestCompletedIntegrationEvent
            {
                TenantId = tenantId,
                CorrelationId = "corr-completed",
                FormRequestId = Guid.NewGuid(),
                Participant = CustomerParticipant(customerId),
                Title = "Tax organizer",
                RevisionNumber = 1,
                CompletedByUserId = Guid.NewGuid(),
            },
            sender,
            new Resolver(),
            new Correlation(),
            CancellationToken.None
        );

        var email = Assert.Single(gateway.Queued);
        Assert.Equal("client@example.com", email.To);
        Assert.Equal("Es", render.Variables["language"]);
        Assert.DoesNotContain(metrics.NoRecipient, channel => channel == "Email");
    }

    [Fact]
    public async Task Push_render_exception_keeps_email_and_in_app_and_does_not_throw()
    {
        var push = new CapturingPushSender();
        var harness = new NotificationDispatcherHarness(
            push,
            new SinglePushDeviceRepository(),
            new MemoryNotificationLogs()
        );
        var gateway = new EmailGateway();
        var metrics = new RecordingMetrics();
        var logger = new CapturingLogger<ClientFormNotificationSender>();
        var sender = Sender(
            harness,
            new RenderClient { PushFailure = PushRenderFailure.Throws },
            gateway,
            userEmails: new UserEmailResolver("staff@example.com"),
            settings: new ClientFormSettings(true),
            logger: logger,
            metrics: metrics
        );

        await sender.SendAsync(StaffRequest(NotificationKinds.TasksFormRequestSubmitted), CancellationToken.None);

        Assert.Single(gateway.Queued);
        Assert.Single(harness.Publisher.Published);
        Assert.Empty(push.Messages);
        Assert.Equal(["render_failed"], metrics.PushFailures);
        Assert.Single(logger.Messages);
    }

    [Fact]
    public async Task Push_render_failure_result_is_recorded_without_sending()
    {
        var push = new CapturingPushSender();
        var metrics = new RecordingMetrics();
        var sender = Sender(
            new NotificationDispatcherHarness(push, new SinglePushDeviceRepository(), new MemoryNotificationLogs()),
            new RenderClient { PushFailure = PushRenderFailure.ReturnsFailure },
            settings: new ClientFormSettings(true),
            metrics: metrics
        );

        await sender.SendAsync(StaffRequest(NotificationKinds.TasksFormActionFailed), CancellationToken.None);

        Assert.Empty(push.Messages);
        Assert.Equal(["render_failed"], metrics.PushFailures);
    }

    [Fact]
    public async Task Staff_submission_email_keeps_the_client_name_from_the_event()
    {
        var staff = Guid.NewGuid();
        var render = new RenderClient();
        var gateway = new EmailGateway();
        var sender = Sender(
            new NotificationDispatcherHarness(),
            render,
            gateway,
            userEmails: new UserEmailResolver("staff@example.com")
        );

        await FormRequestSubmittedConsumer.Handle(
            Submitted(Guid.NewGuid(), Guid.NewGuid(), staff, null),
            sender,
            new Assignments(staff),
            new Resolver().Allow(BuildingBlocks.Authorization.TasksPermissions.FormRequestsReview, staff),
            new Correlation(),
            CancellationToken.None
        );

        Assert.Single(gateway.Queued);
        Assert.Equal("task.form_request_submitted.v1", render.EventKey);
        Assert.Equal("Client", render.Variables["customer_name"]);
    }

    [Fact]
    public async Task Push_provider_exception_keeps_email_and_in_app_and_retries_that_device()
    {
        var devices = new SinglePushDeviceRepository();
        var harness = new NotificationDispatcherHarness(
            new ThrowingPushSender(),
            devices,
            new MemoryNotificationLogs()
        );
        var gateway = new EmailGateway();
        var retries = new RecordingRetryScheduler();
        var sender = Sender(
            harness,
            new RenderClient(),
            gateway,
            userEmails: new UserEmailResolver("staff@example.com"),
            settings: new ClientFormSettings(true),
            retryScheduler: retries
        );
        var request = StaffRequest(NotificationKinds.TasksFormRequestSubmitted);

        await sender.SendAsync(request, CancellationToken.None);

        Assert.Single(gateway.Queued);
        Assert.Single(harness.Publisher.Published);
        var retry = Assert.Single(retries.Scheduled);
        var device = Assert.Single(
            await devices.ListActiveForUserAsync(request.TenantId, request.RecipientUserId!.Value)
        );
        Assert.Equal([device.Id], retry.Request.Request.DeviceIds);
    }

    [Fact]
    public async Task In_app_only_kind_does_not_resolve_email_or_host()
    {
        var userEmails = new CountingUserEmailResolver();
        var hosts = new CountingHostResolver();
        var harness = new NotificationDispatcherHarness();
        var sender = Sender(harness, userEmails: userEmails, hosts: hosts);

        await sender.SendAsync(
            StaffRequest(NotificationKinds.TasksFormAttachmentRejectedStaff),
            CancellationToken.None
        );

        Assert.Equal(0, userEmails.Calls);
        Assert.Equal(0, hosts.Calls);
        Assert.Single(harness.Publisher.Published);
    }

    [Fact]
    public async Task Staff_client_form_kinds_use_the_client_requests_category()
    {
        var preferences = new CategoryRecordingPreferences();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var staff = Guid.NewGuid();
        var sender = Sender(
            new NotificationDispatcherHarness(preferences: preferences),
            userEmails: new UserEmailResolver("staff@example.com"),
            preferences: preferences
        );
        var resolver = new Resolver().Allow(BuildingBlocks.Authorization.TasksPermissions.FormRequestsReview, staff);

        await FormRequestSubmittedConsumer.Handle(
            Submitted(tenantId, customerId, staff, null),
            sender,
            new Assignments(staff),
            resolver,
            new Correlation(),
            CancellationToken.None
        );
        await FormActionFailedConsumer.Handle(
            new FormActionFailedIntegrationEvent
            {
                TenantId = tenantId,
                CorrelationId = "corr-failed",
                FormRequestId = Guid.NewGuid(),
                ActionType = "CreateCustomer",
                ErrorCode = "Customer.Unavailable",
                ResponsibleUserId = staff,
            },
            sender,
            resolver,
            new Correlation(),
            CancellationToken.None
        );

        Assert.NotEmpty(preferences.Categories);
        Assert.All(preferences.Categories, category => Assert.Equal(NotificationCategory.ClientRequests, category));
    }

    private static ClientFormNotificationSender Sender(
        NotificationDispatcherHarness harness,
        RenderClient? render = null,
        EmailGateway? gateway = null,
        ICustomerEmailDirectoryRepository? customers = null,
        ICustomerContactPolicyRepository? policies = null,
        IUserEmailResolver? userEmails = null,
        IUserNotificationPreferenceRepository? preferences = null,
        IClientFormNotificationSettingsRepository? settings = null,
        IClientFormPushRetryScheduler? retryScheduler = null,
        Microsoft.Extensions.Logging.ILogger<ClientFormNotificationSender>? logger = null,
        INotificationDispatchMetrics? metrics = null,
        ITenantHostResolver? hosts = null
    ) =>
        new(
            customers ?? new CustomerDirectory(null),
            policies ?? new ContactPolicies(),
            userEmails ?? new NoUserEmailResolver(),
            preferences ?? new Preferences(true),
            settings ?? new ClientFormSettings(false),
            gateway ?? new EmailGateway(),
            render ?? new RenderClient(),
            harness.Dispatcher,
            hosts ?? new HostResolver(),
            Options.Create(new PortalOptions()),
            metrics ?? new NoOpMetrics(),
            new Correlation(),
            retryScheduler ?? new NoOpRetryScheduler(),
            logger ?? NullLogger<ClientFormNotificationSender>.Instance
        );

    private static ClientFormNotificationRequest StaffRequest(string kind)
    {
        var requestId = Guid.NewGuid();
        return new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            requestId,
            Guid.NewGuid(),
            DateTime.UtcNow,
            kind,
            "task.form_request_submitted.v1",
            "task.form_request_submitted.push.v1",
            "Submission received",
            "A form is ready for review.",
            new Dictionary<string, object?>(),
            new Dictionary<string, string>(),
            NotificationCategory.ClientRequests
        );
    }

    [Fact]
    public async Task Participant_notice_passes_the_external_participant_language_to_scribe()
    {
        var codec = RealCodec();
        var render = new RenderClient();

        await FormParticipantNotificationRequestedConsumer.Handle(
            ParticipantNotice(codec, FormParticipantNotificationKinds.Reminder, new Dictionary<string, string>(), "Es"),
            codec,
            Sender(new NotificationDispatcherHarness(), render, new EmailGateway()),
            new Correlation(),
            NullLogger<FormParticipantNotificationRequestedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Equal("Es", render.Variables["language"]);
    }

    private static FormParticipantNotificationRequestedIntegrationEvent ParticipantNotice(
        IProtectedPayloadCodec codec,
        string kind,
        IReadOnlyDictionary<string, string> parameters,
        string language = "En"
    )
    {
        var tenantId = Guid.NewGuid();
        var formRequestId = Guid.NewGuid();
        var participant = ExternalParticipant(Guid.NewGuid());
        var commandId = Guid.NewGuid();
        return new FormParticipantNotificationRequestedIntegrationEvent
        {
            TenantId = tenantId,
            CorrelationId = "corr-participant-notice",
            CommandId = commandId,
            FormRequestId = formRequestId,
            Participant = participant,
            Kind = kind,
            Payload = codec.Protect(
                ClientFormProtectedPayloadSchemas.ParticipantNotification,
                new ProtectedPayloadBinding(tenantId, formRequestId, participant),
                new FormParticipantNotificationPayload("guest@example.com", "Guest", "Intake", parameters, language)
            ),
        };
    }

    private static ClientFormParticipant ExternalParticipant(Guid participantId) =>
        new() { Kind = "External", ParticipantId = participantId };

    private static IProtectedPayloadCodec RealCodec() =>
        new BuildingBlocks.Infrastructure.Security.JsonProtectedPayloadCodec(
            new BuildingBlocks.Infrastructure.Security.AesGcmRotatingSecretProtector(
                Options.Create(
                    new BuildingBlocks.Infrastructure.Security.RotatingSecretProtectionOptions
                    {
                        MasterKey = Convert.ToBase64String(
                            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)
                        ),
                        MasterKeyVersion = 1,
                    }
                )
            )
        );

    private static FormRequestSubmittedIntegrationEvent Submitted(
        Guid tenantId,
        Guid customerId,
        Guid requestedByUserId,
        Guid? responsibleUserId
    ) =>
        new()
        {
            TenantId = tenantId,
            CorrelationId = "corr-submitted",
            FormRequestId = Guid.NewGuid(),
            Participant = CustomerParticipant(customerId),
            ResponsibleUserId = responsibleUserId,
            CustomerDisplayName = "Client",
            Title = "Tax organizer",
            RevisionNumber = 1,
            IsResubmission = false,
            RequestedByUserId = requestedByUserId,
            SubmittedBy = new ClientFormActorRef { Kind = "CustomerPortal", PortalUserId = Guid.NewGuid() },
        };

    private static ClientFormParticipant CustomerParticipant(Guid customerId) =>
        new() { Kind = "Customer", CustomerId = customerId };

    private sealed class Assignments(params Guid[] users) : ICustomerAssignmentAudienceRepository
    {
        public Task<IReadOnlyCollection<Guid>> GetAssignedUserIdsAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyCollection<Guid>>(users);
    }

    private sealed class Resolver : IRecipientResolver
    {
        private readonly Dictionary<string, HashSet<Guid>> allowed = new(StringComparer.Ordinal);

        public Resolver Allow(string permission, params Guid[] users)
        {
            allowed[permission] = [.. users];
            return this;
        }

        public Task<IReadOnlyList<Guid>> ResolveAsync(ByPermission audience, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<Result<IReadOnlyList<Guid>>> ResolveAsync(
            NotificationAudience audience,
            RecipientValidation validation,
            CancellationToken ct = default
        )
        {
            if (audience is not Participants participants || validation.RequiredCapability is null)
                return Task.FromResult(Result.Success<IReadOnlyList<Guid>>([]));
            allowed.TryGetValue(validation.RequiredCapability, out var permitted);
            var result = permitted is null ? [] : participants.UserIds.Where(permitted.Contains).ToArray();
            return Task.FromResult(Result.Success<IReadOnlyList<Guid>>(result));
        }
    }

    private sealed class CustomerDirectory(CustomerEmailDirectoryEntry? entry) : ICustomerEmailDirectoryRepository
    {
        public Task<CustomerEmailDirectoryEntry?> GetByCustomerIdAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult(entry);

        public Task AddAsync(CustomerEmailDirectoryEntry entry, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class ContactPolicies(CustomerContactPolicyProjection? policy = null)
        : ICustomerContactPolicyRepository
    {
        public Task<CustomerContactPolicyProjection?> GetByCustomerIdAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult(policy);

        public Task AddAsync(CustomerContactPolicyProjection row, CancellationToken ct = default) => Task.CompletedTask;
    }

    private enum PushRenderFailure
    {
        None,
        Throws,
        ReturnsFailure,
    }

    private sealed class RenderClient : IScribeRenderClient
    {
        public PushRenderFailure PushFailure { get; init; }
        public int Calls { get; private set; }
        public int TextCalls { get; private set; }
        public string? EventKey { get; private set; }
        public IReadOnlyDictionary<string, object?> Variables { get; private set; } = new Dictionary<string, object?>();

        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        )
        {
            Calls++;
            EventKey = eventKey;
            Variables = variables;
            return Task.FromResult(Result.Success(new ScribeRenderedEmail("Subject", "<p>Body</p>", "Body")));
        }

        public Task<Result<ScribeRenderedText>> RenderTextAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        )
        {
            TextCalls++;
            if (PushFailure == PushRenderFailure.Throws)
                throw new HttpRequestException("Scribe unavailable");
            if (PushFailure == PushRenderFailure.ReturnsFailure)
                return Task.FromResult(
                    Result.Failure<ScribeRenderedText>(new Error("Notification.ScribeRender", "Render failed."))
                );
            EventKey = eventKey;
            Variables = variables;
            return Task.FromResult(Result.Success(new ScribeRenderedText("Push title", "Push body")));
        }
    }

    private sealed class NoUserEmailResolver : IUserEmailResolver
    {
        public Task<string?> ResolveAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class UserEmailResolver(string? email) : IUserEmailResolver
    {
        public Task<string?> ResolveAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(email);
    }

    private sealed class Preferences(bool enabled) : IUserNotificationPreferenceRepository
    {
        public Task<bool> IsEnabledAsync(
            Guid tenantId,
            Guid userId,
            NotificationCategory category,
            NotificationChannel channel,
            CancellationToken ct = default
        ) => Task.FromResult(enabled);

        public Task<UserNotificationPreference?> GetAsync(
            Guid tenantId,
            Guid userId,
            NotificationCategory category,
            NotificationChannel channel,
            CancellationToken ct = default
        ) => Task.FromResult<UserNotificationPreference?>(null);

        public Task<IReadOnlyList<UserNotificationPreference>> ListForUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<UserNotificationPreference>>([]);

        public Task AddAsync(UserNotificationPreference preference, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class ClientFormSettings(bool enabled) : IClientFormNotificationSettingsRepository
    {
        public Task<bool> IsPushEnabledAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(enabled);

        public Task<ClientFormNotificationSettingsProjection?> GetAsync(
            Guid tenantId,
            CancellationToken ct = default
        ) => Task.FromResult<ClientFormNotificationSettingsProjection?>(null);

        public Task AddAsync(ClientFormNotificationSettingsProjection row, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class NoOpMetrics : INotificationDispatchMetrics
    {
        public void RecordSent(string kind, string channel) { }

        public void RecordSuppressedByPreference(string kind, string channel) { }

        public void RecordFailed(string kind, string channel) { }

        public void RecordNoRecipient(string kind, string channel) { }
    }

    private sealed class RecordingMetrics : INotificationDispatchMetrics
    {
        public List<string> NoRecipient { get; } = [];
        public List<string> PushFailures { get; } = [];

        public void RecordSent(string kind, string channel) { }

        public void RecordSuppressedByPreference(string kind, string channel) { }

        public void RecordFailed(string kind, string channel) { }

        public void RecordNoRecipient(string kind, string channel) => NoRecipient.Add(channel);

        public void RecordClientFormPushFailure(string code) => PushFailures.Add(code);
    }

    private sealed class CountingUserEmailResolver : IUserEmailResolver
    {
        public int Calls { get; private set; }

        public Task<string?> ResolveAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<string?>("staff@example.com");
        }
    }

    private sealed class CountingHostResolver : ITenantHostResolver
    {
        public int Calls { get; private set; }

        public Task<string?> ResolveHostAsync(Guid tenantId, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<string?>("office.taxproffice.com");
        }
    }

    private sealed class CategoryRecordingPreferences : IUserNotificationPreferenceRepository
    {
        public List<NotificationCategory> Categories { get; } = [];

        public Task<bool> IsEnabledAsync(
            Guid tenantId,
            Guid userId,
            NotificationCategory category,
            NotificationChannel channel,
            CancellationToken ct = default
        )
        {
            Categories.Add(category);
            return Task.FromResult(true);
        }

        public Task<UserNotificationPreference?> GetAsync(
            Guid tenantId,
            Guid userId,
            NotificationCategory category,
            NotificationChannel channel,
            CancellationToken ct = default
        ) => Task.FromResult<UserNotificationPreference?>(null);

        public Task<IReadOnlyList<UserNotificationPreference>> ListForUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<UserNotificationPreference>>([]);

        public Task AddAsync(UserNotificationPreference preference, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class NoOpRetryScheduler : IClientFormPushRetryScheduler
    {
        public Task ScheduleAsync(RetryClientFormPushRequested request, TimeSpan delay) => Task.CompletedTask;
    }

    private sealed class RecordingRetryScheduler : IClientFormPushRetryScheduler
    {
        public List<(RetryClientFormPushRequested Request, TimeSpan Delay)> Scheduled { get; } = [];

        public Task ScheduleAsync(RetryClientFormPushRequested request, TimeSpan delay)
        {
            Scheduled.Add((request, delay));
            return Task.CompletedTask;
        }
    }

    private sealed class SequencePushSender(Error firstFailure) : IPushSender
    {
        public List<PushMessage> Messages { get; } = [];

        public Task<Result> SendAsync(PushMessage message, CancellationToken ct = default)
        {
            Messages.Add(message);
            return Task.FromResult(Messages.Count == 1 ? Result.Failure(firstFailure) : Result.Success());
        }
    }

    private sealed class ThrowingPushSender : IPushSender
    {
        public Task<Result> SendAsync(PushMessage message, CancellationToken ct = default) =>
            throw new HttpRequestException("FCM unreachable");
    }

    private sealed class CapturingPushSender : IPushSender
    {
        public List<PushMessage> Messages { get; } = [];

        public Task<Result> SendAsync(PushMessage message, CancellationToken ct = default)
        {
            Messages.Add(message);
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class SinglePushDeviceRepository : IPushDeviceTokenRepository
    {
        private readonly PushDeviceToken device;
        private PushDeviceToken? registered;

        public SinglePushDeviceRepository()
        {
            device = PushDeviceToken
                .Register(Guid.NewGuid(), Guid.NewGuid(), PushPlatform.Web, "web-token", null)
                .Value;
        }

        public Task AddAsync(PushDeviceToken token, CancellationToken ct = default) => Task.CompletedTask;

        public Task<PushDeviceToken?> FindByTokenAsync(Guid tenantId, string token, CancellationToken ct = default) =>
            Task.FromResult<PushDeviceToken?>(null);

        public Task<PushDeviceToken?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
            Task.FromResult<PushDeviceToken?>(null);

        public Task<IReadOnlyList<PushDeviceToken>> ListActiveForUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        )
        {
            // El mismo dispositivo (mismo Id) en cada consulta, como la tabla real.
            registered ??= PushDeviceToken.Register(tenantId, userId, PushPlatform.Web, device.Token, null).Value;
            return Task.FromResult<IReadOnlyList<PushDeviceToken>>([registered]);
        }

        public Task RevokeAsync(Guid tenantId, Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class MemoryNotificationLogs : INotificationLogRepository
    {
        private readonly List<NotificationLog> logs = [];

        public Task AddAsync(NotificationLog log, CancellationToken ct = default)
        {
            logs.Add(log);
            return Task.CompletedTask;
        }

        public Task<(IReadOnlyList<NotificationLog> Items, int TotalCount)> GetPagedAsync(
            Guid tenantId,
            NotificationStatus? status,
            int page,
            int size,
            CancellationToken ct = default
        ) => Task.FromResult<(IReadOnlyList<NotificationLog>, int)>((logs, logs.Count));

        public Task<NotificationLog?> GetByRelatedEventIdAsync(
            Guid tenantId,
            Guid relatedEventId,
            string templateKey,
            string recipient,
            CancellationToken ct = default
        ) => Task.FromResult<NotificationLog?>(null);

        public Task<NotificationLog?> GetByRelatedEventAsync(
            Guid tenantId,
            Guid relatedEventId,
            string kind,
            NotificationChannel channel,
            string recipient,
            CancellationToken ct = default
        ) => Task.FromResult<NotificationLog?>(null);
    }

    private sealed class EmailGateway : IEmailDispatchGateway
    {
        public List<EmailDispatchRequest> Queued { get; } = [];

        public Task<EmailDispatchResult> QueueEmailAsync(EmailDispatchRequest request, CancellationToken ct = default)
        {
            Queued.Add(request);
            return Task.FromResult(
                new EmailDispatchResult(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    NotificationDispatchAttemptStatus.Queued,
                    null,
                    null
                )
            );
        }
    }

    private sealed class HostResolver : ITenantHostResolver
    {
        public Task<string?> ResolveHostAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<string?>("office.taxproffice.com");
    }

    private sealed class Correlation : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = "test";

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

    private sealed class CapturingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Warning)
                Messages.Add(formatter(state, exception));
        }
    }

    private sealed class AccessCodeCodec(FormAccessCodeDeliveryPayload payload) : IProtectedPayloadCodec
    {
        public ProtectedPayloadBinding? Binding { get; private set; }

        public ProtectedPayload Protect<T>(string schema, ProtectedPayloadBinding binding, T content) =>
            throw new NotSupportedException();

        public T Unprotect<T>(
            ProtectedPayload protectedPayload,
            string expectedSchema,
            ProtectedPayloadBinding expectedBinding
        )
        {
            Assert.Equal(ClientFormProtectedPayloadSchemas.AccessCode, expectedSchema);
            Binding = expectedBinding;
            return Assert.IsType<T>(payload);
        }
    }
}
