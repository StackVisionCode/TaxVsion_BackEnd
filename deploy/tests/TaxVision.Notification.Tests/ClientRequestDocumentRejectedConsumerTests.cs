using BuildingBlocks.Messaging.NotificationIntegrationEvents;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
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
            harness.Dispatcher,
            NoPortalRecipientResolver.Instance,
            Options.Create(new PortalOptions()),
            new FakeTenantHostResolver(),
            FixedRecipientLocale.English,
            new NoOpCorrelation(),
            CancellationToken.None
        );

        var inApp = Assert.Single(harness.Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>());
        Assert.Equal(withStaffRecipient ? Successor : RequestedBy, inApp.RecipientUserId);
    }

    /// <summary>El correo al cliente sale en su idioma, con el mensaje del sistema también en su idioma.</summary>
    [Fact]
    public async Task Email_is_rendered_with_the_recipient_language()
    {
        var scribe = new RecordingScribeRenderClient();
        var gateway = new RecordingEmailGateway();
        var evt = Rejected(null);
        var locales = new FixedRecipientLocale("es", "America/Bogota");

        await ClientRequestDocumentRejectedConsumer.Handle(
            evt,
            new OneCustomer(evt.CustomerId),
            AllowAllContactPolicyRepository.Instance,
            new NotificationDispatcherHarness(emailGateway: gateway, scribe: scribe, locales: locales).Dispatcher,
            NoPortalRecipientResolver.Instance,
            Options.Create(new PortalOptions()),
            new FakeTenantHostResolver(),
            locales,
            new NoOpCorrelation(),
            CancellationToken.None
        );

        var render = Assert.Single(scribe.Renders);
        Assert.Equal("Es", render.Language);
        Assert.Equal(new ScribeReader("es", "America/Bogota"), render.Reader);
        Assert.Equal(
            "No pudimos procesar este archivo. Vuelve a subirlo, por favor.",
            render.Variables["client_message"]
        );
        Assert.Single(gateway.Queued);
    }

    private sealed class OneCustomer(Guid customerId) : ICustomerEmailDirectoryRepository
    {
        public Task<CustomerEmailDirectoryEntry?> GetByCustomerIdAsync(
            Guid tenantId,
            Guid id,
            CancellationToken ct = default
        ) =>
            Task.FromResult<CustomerEmailDirectoryEntry?>(
                id == customerId
                    ? CustomerEmailDirectoryEntry.Create(tenantId, id, "client@example.com", "Client")
                    : null
            );

        public Task AddAsync(CustomerEmailDirectoryEntry entry, CancellationToken ct = default) => Task.CompletedTask;
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
}
