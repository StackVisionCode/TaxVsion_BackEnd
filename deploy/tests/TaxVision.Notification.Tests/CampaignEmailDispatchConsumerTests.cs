using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Application.Email.Sending.Campaign;
using TaxVision.Notification.Application.Email.Sending.Consumers;
using TaxVision.Notification.Domain.Directory;
using TaxVision.Notification.Domain.Emailing.Campaigns;

namespace TaxVision.Notification.Tests;

/// <summary>
/// El correo de campaña respeta la política de contacto del cliente (D-CMP-3: se filtra en el envío).
/// Un cliente con el correo apagado no recibe nada y la unidad vuelve a Campaigns como Skipped.
/// </summary>
public sealed class CampaignEmailDispatchConsumerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();

    private static CampaignDispatchRequestedIntegrationEvent Dispatch(string contactRef) =>
        new()
        {
            TenantId = Tenant,
            CorrelationId = "corr",
            CampaignId = Guid.NewGuid(),
            RunId = Guid.NewGuid(),
            RecipientId = Guid.NewGuid(),
            AttemptNo = 1,
            DispatchId = "d-1",
            Channel = "Email",
            ContactRef = contactRef,
            Email = "client@example.com",
            Subject = "Hello",
            Body = "Body",
        };

    [Fact]
    public async Task Handle_CustomerEmailDisabled_ReportsSkipped()
    {
        var provider = new RecordingCampaignEmailProvider();
        var policies = new PolicyRepository(
            CustomerContactPolicyProjection.Create(Tenant, Customer, emailEnabled: false, smsEnabled: true)
        );

        var result = await Handle(Dispatch(Customer.ToString("N")), provider, policies);

        Assert.Empty(provider.Sent);
        Assert.Equal("Skipped", result!.Outcome);
        Assert.Equal("contact_policy", result.Reason);
    }

    [Fact]
    public async Task Handle_CustomerEmailEnabled_SendsAndReportsAccepted()
    {
        var provider = new RecordingCampaignEmailProvider();
        var policies = new PolicyRepository(
            CustomerContactPolicyProjection.Create(Tenant, Customer, emailEnabled: true, smsEnabled: false)
        );

        var result = await Handle(Dispatch(Customer.ToString("N")), provider, policies);

        Assert.Single(provider.Sent);
        Assert.Equal("Accepted", result!.Outcome);
    }

    [Fact]
    public async Task Handle_ContactWithoutPolicy_Sends()
    {
        // Un contacto guardado (o manual) no tiene fila en la proyección: la política nula permite.
        var provider = new RecordingCampaignEmailProvider();

        var result = await Handle(Dispatch("manual-ref"), provider, new PolicyRepository());

        Assert.Single(provider.Sent);
        Assert.Equal("Accepted", result!.Outcome);
    }

    private static Task<CampaignDispatchResultIntegrationEvent?> Handle(
        CampaignDispatchRequestedIntegrationEvent evt,
        ICampaignEmailProvider provider,
        ICustomerContactPolicyRepository policies
    ) =>
        CampaignEmailDispatchConsumer.Handle(
            evt,
            provider,
            policies,
            new InMemorySends(),
            new NoSaveUnitOfWork(),
            Options.Create(new CampaignDispatchOptions()),
            NullLogger<CampaignEmailMessage>.Instance,
            CancellationToken.None
        );

    private sealed class InMemorySends : ICampaignDispatchSendRepository
    {
        private readonly List<CampaignDispatchSend> _rows = [];

        public Task<CampaignDispatchSend?> FindAsync(
            Guid tenantId,
            string dispatchId,
            CancellationToken ct = default
        ) => Task.FromResult(_rows.FirstOrDefault(x => x.TenantId == tenantId && x.DispatchId == dispatchId));

        public Task AddAsync(CampaignDispatchSend send, CancellationToken ct = default)
        {
            _rows.Add(send);
            return Task.CompletedTask;
        }
    }

    private sealed class NoSaveUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class RecordingCampaignEmailProvider : ICampaignEmailProvider
    {
        public List<CampaignEmailMessage> Sent { get; } = [];
        public string Code => "fake";

        public Task<CampaignEmailSendResult> SendAsync(CampaignEmailMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.FromResult(CampaignEmailSendResult.Ok("pm-1"));
        }
    }

    private sealed class PolicyRepository(params CustomerContactPolicyProjection[] rows)
        : ICustomerContactPolicyRepository
    {
        public Task<CustomerContactPolicyProjection?> GetByCustomerIdAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult(rows.FirstOrDefault(r => r.TenantId == tenantId && r.CustomerId == customerId));

        public Task<IReadOnlyCollection<Guid>> GetExistingCustomerIdsAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> customerIds,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyCollection<Guid>>([]);

        public Task AddAsync(CustomerContactPolicyProjection row, CancellationToken ct = default) => Task.CompletedTask;
    }
}
