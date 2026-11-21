using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.TimeZones;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Messages;
using TaxVision.Correspondence.Tests.Files;
using TaxVision.Correspondence.Tests.Ingest;
using TaxVision.Correspondence.Tests.Projections;

namespace TaxVision.Correspondence.Tests.Messages;

// File Library: guardar un adjunto entrante lo declara como incoming_email/incoming_attachment.
public sealed class CompleteAttachmentDownloadSourceTests
{
    private readonly Guid _tenant = Guid.NewGuid();

    private async Task<SaveFileRequestedIntegrationEvent> CompleteAsync(Guid? customer)
    {
        var thread = MailFileTestData.Thread(_tenant, customer);
        var email = MailFileTestData.Incoming(thread, Guid.NewGuid(), null);
        var repo = new FakeIncomingEmailRepository();
        await repo.AddAsync(email);
        var bus = new FakeMessageBus();

        var result = await CompleteAttachmentDownloadHandler.Handle(
            new CompleteAttachmentDownloadCommand(
                _tenant,
                email.Id,
                email.Attachments.Single().Id,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "temp",
                "key",
                10,
                "corr"
            ),
            repo,
            bus,
            new FakeUnitOfWork(),
            new UtcZone(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var saved = Assert.IsType<SaveFileRequestedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(email.Id, saved.SourceResourceId);
        return saved;
    }

    [Fact]
    public async Task Saving_an_attachment_of_a_customer_declares_it_with_the_customer()
    {
        var customer = Guid.NewGuid();

        var saved = await CompleteAsync(customer);

        Assert.Equal("incoming_email", saved.SourceResourceType);
        Assert.Equal("incoming_attachment", saved.SourceRole);
        Assert.Equal(customer, saved.RelatedCustomerId);
        Assert.Equal("Customer", saved.OwnerType);
    }

    [Fact]
    public async Task Saving_an_attachment_without_customer_declares_it_without_customer()
    {
        var saved = await CompleteAsync(null);

        Assert.Equal("incoming_email", saved.SourceResourceType);
        Assert.Equal("incoming_attachment", saved.SourceRole);
        Assert.Null(saved.RelatedCustomerId);
        Assert.Equal("User", saved.OwnerType);
    }

    private sealed class UtcZone : IOfficeTimeZone
    {
        public Task<ResolvedTimeZone> ResolveAsync(Guid tenantId, CancellationToken ct) =>
            Task.FromResult(ResolvedTimeZone.Resolve(null));
    }
}
