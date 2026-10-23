using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers.ClientForms;
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Domain.Directory;
using TaxVision.Notification.Domain.Notifications;
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
            harness.Dispatcher,
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
            harness.Dispatcher,
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
            harness.Dispatcher,
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
            harness.Dispatcher,
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
            harness.Dispatcher,
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
            new CustomerDirectory(
                CustomerEmailDirectoryEntry.Create(tenantId, customerId, "client@example.com", "Client")
            ),
            new ContactPolicies(
                CustomerContactPolicyProjection.Create(tenantId, customerId, emailEnabled: false, smsEnabled: true)
            ),
            gateway,
            render,
            harness.Dispatcher,
            new Resolver(),
            Options.Create(new PortalOptions()),
            new HostResolver(),
            new Correlation(),
            CancellationToken.None
        );

        Assert.Equal(0, render.Calls);
        Assert.Empty(gateway.Queued);
        Assert.Empty(harness.Publisher.Published);
    }

    [Fact]
    public async Task Attachment_rejection_uses_safe_message_without_file_name_or_free_text()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var render = new RenderClient();
        var gateway = new EmailGateway();

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
            new CustomerDirectory(
                CustomerEmailDirectoryEntry.Create(tenantId, customerId, "client@example.com", "Client")
            ),
            new ContactPolicies(),
            gateway,
            render,
            new NotificationDispatcherHarness().Dispatcher,
            new Assignments(),
            new Resolver(),
            Options.Create(new PortalOptions()),
            new HostResolver(),
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
            gateway,
            render,
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
            gateway,
            render,
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
            gateway,
            render,
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
            gateway,
            new RenderClient(),
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
            gateway,
            render,
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
            new EmailGateway(),
            render,
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
            ParticipantNotice(codec, "reminder", new Dictionary<string, string>()),
            codec,
            gateway,
            render,
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
            gateway,
            new RenderClient(),
            new Correlation(),
            logger,
            CancellationToken.None
        );

        Assert.Empty(gateway.Queued);
        var warning = Assert.Single(logger.Messages);
        Assert.StartsWith("Security:", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("guest@example.com", warning, StringComparison.Ordinal);
    }

    private static FormParticipantNotificationRequestedIntegrationEvent ParticipantNotice(
        IProtectedPayloadCodec codec,
        string kind,
        IReadOnlyDictionary<string, string> parameters
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
                new FormParticipantNotificationPayload("guest@example.com", "Guest", "Intake", parameters)
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

    private sealed class RenderClient : IScribeRenderClient
    {
        public int Calls { get; private set; }
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
