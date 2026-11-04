using BuildingBlocks.Messaging.NotificationIntegrationEvents;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Domain.Directory;
using TaxVision.Notification.Tests.Lifecycle;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>
/// El aviso in-app al staff del archivo rechazado va a <c>StaffRecipientUserId</c> si viene; si no
/// (mensajes viejos), a quien hizo el pedido.
/// </summary>
public sealed class ClientRequestDocumentRejectedConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid RequestedBy = Guid.NewGuid();
    private static readonly Guid Successor = Guid.NewGuid();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Staff_notice_goes_to_the_staff_recipient_or_the_requester(bool withStaffRecipient)
    {
        var harness = new NotificationDispatcherHarness();

        await ClientRequestDocumentRejectedConsumer.Handle(
            Rejected(withStaffRecipient ? Successor : null),
            new NoCustomers(),
            AllowAllContactPolicyRepository.Instance,
            new UnusedGateway(),
            new UnusedScribe(),
            harness.Dispatcher,
            NoPortalRecipientResolver.Instance,
            Options.Create(new PortalOptions()),
            new FakeTenantHostResolver(),
            new NoOpCorrelation(),
            CancellationToken.None
        );

        var inApp = Assert.Single(harness.Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>());
        Assert.Equal(withStaffRecipient ? Successor : RequestedBy, inApp.RecipientUserId);
    }

    private static ClientRequestDocumentRejectedIntegrationEvent Rejected(Guid? staffRecipient) =>
        new()
        {
            TenantId = TenantId,
            CorrelationId = "corr",
            ClientRequestId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            FileId = Guid.NewGuid(),
            DisplayName = "w2.pdf",
            Reason = "malware",
            ClientMessage = "We couldn't process your file.",
            RequestedByUserId = RequestedBy,
            StaffRecipientUserId = staffRecipient,
        };

    private sealed class NoCustomers : ICustomerEmailDirectoryRepository
    {
        public Task<CustomerEmailDirectoryEntry?> GetByCustomerIdAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult<CustomerEmailDirectoryEntry?>(null);

        public Task AddAsync(CustomerEmailDirectoryEntry entry, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class UnusedGateway : IEmailDispatchGateway
    {
        public Task<EmailDispatchResult> QueueEmailAsync(
            EmailDispatchRequest request,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("No customer email expected in this test.");
    }

    private sealed class UnusedScribe : IScribeRenderClient
    {
        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("No render expected in this test.");
    }
}
