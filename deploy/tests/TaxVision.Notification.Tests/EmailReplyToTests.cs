using BuildingBlocks.Messaging.EmailIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Tests.Lifecycle;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>
/// DM-02 (a): un correo de la oficina (<c>TenantPreferred</c>) sin buzón conectado cae al SMTP del sistema solo si
/// el evento trae <c>ReplyTo</c>. El gateway lo pone: el de quien inició si el consumer lo da, si no el del Owner.
/// </summary>
public sealed class EmailReplyToTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static EmailDispatchRequest Request(
        EmailDispatchScope scope,
        string? replyTo = null,
        EmailDispatchStream stream = EmailDispatchStream.Transactional
    ) =>
        new(
            TenantId: TenantId,
            To: "client@example.com",
            Subject: "Invoice",
            HtmlBody: "<p>Invoice</p>",
            TextBody: "Invoice",
            TemplateKey: "billing.invoice_sent",
            RelatedEventId: Guid.NewGuid(),
            CorrelationId: "c",
            Scope: scope,
            Stream: stream,
            ReplyTo: replyTo
        );

    private static async Task<NotificationsEmailSendRequestedIntegrationEvent> QueueAsync(
        EmailDispatchRequest request,
        string? ownerEmail = "owner@office.test"
    )
    {
        var publisher = new CapturingIntegrationEventPublisher();
        var gateway = new EventBasedEmailDispatchGateway(
            publisher,
            new RecordingNotificationLogs(),
            new CountingUnitOfWork(),
            TestDeliveryGates.Open(),
            NullLogger<EventBasedEmailDispatchGateway>.Instance,
            new FixedOfficeReplyTo(ownerEmail)
        );
        await gateway.QueueEmailAsync(request);
        return Assert.Single(publisher.Published.OfType<NotificationsEmailSendRequestedIntegrationEvent>());
    }

    [Fact]
    public async Task An_office_email_without_reply_to_gets_the_owner_so_it_can_fall_back_to_the_system()
    {
        var evt = await QueueAsync(Request(EmailDispatchScope.TenantPreferred));

        Assert.Equal("TenantPreferred", evt.RequiredProviderScope);
        Assert.Equal("owner@office.test", evt.ReplyTo);
    }

    [Fact]
    public async Task The_reply_to_of_whoever_started_it_wins()
    {
        var evt = await QueueAsync(Request(EmailDispatchScope.TenantPreferred, replyTo: "admin@office.test"));

        Assert.Equal("admin@office.test", evt.ReplyTo);
    }

    [Theory]
    [InlineData(EmailDispatchScope.System, EmailDispatchStream.Transactional)]
    [InlineData(EmailDispatchScope.TenantPreferred, EmailDispatchStream.Bulk)]
    public async Task System_emails_and_campaigns_keep_no_reply_to(EmailDispatchScope scope, EmailDispatchStream stream)
    {
        var evt = await QueueAsync(Request(scope, stream: stream));

        Assert.Null(evt.ReplyTo);
    }

    private sealed class FixedOfficeReplyTo(string? email) : IOfficeReplyToResolver
    {
        public Task<string?> ResolveAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(email);
    }
}
