using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers.Commands.BulkChangeStatus;
using static TaxVision.Customer.Tests.Application.CustomerAvatarTestDoubles;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Application;

public sealed class BulkChangeStatusHandlerTests
{
    private static readonly Guid Admin = Guid.NewGuid();

    [Fact]
    public async Task Bulk_archive_requests_deletion_of_each_archived_customer_picture()
    {
        var withPhoto = WithPicture(out var photo);
        var withoutPhoto = NewCustomer();
        var bus = new FakeMessageBus();
        var unitOfWork = new RecordingUnitOfWork(bus);

        var result = await RunAsync(BulkStatusAction.Archive, bus, unitOfWork, withPhoto, withoutPhoto);

        Assert.Equal(2, result.Value.Succeeded);
        Assert.Null(withPhoto.ProfilePictureFileId);
        Assert.Equal(0, unitOfWork.PublishedAtFirstSave);
        var deletion = Assert.Single(bus.Published.OfType<FilesDeletionRequestedIntegrationEvent>());
        Assert.Equal(photo, Assert.Single(deletion.Files).FileId);
        Assert.Equal(withPhoto.Id, deletion.SourceReferenceId);
    }

    [Fact]
    public async Task Bulk_deactivate_keeps_the_picture()
    {
        var customer = WithPicture(out var photo);
        var bus = new FakeMessageBus();

        await RunAsync(BulkStatusAction.Deactivate, bus, new RecordingUnitOfWork(bus), customer);

        Assert.Equal(photo, customer.ProfilePictureFileId);
        Assert.Empty(bus.Published.OfType<FilesDeletionRequestedIntegrationEvent>());
    }

    private static Task<BuildingBlocks.Results.Result<BulkStatusActionResponse>> RunAsync(
        BulkStatusAction action,
        FakeMessageBus bus,
        RecordingUnitOfWork unitOfWork,
        params DomainCustomer[] customers
    ) =>
        BulkChangeStatusHandler.Handle(
            new BulkChangeStatusCommand(TenantId, Admin, action, customers.Select(c => c.Id).ToList(), null),
            new BulkCustomers(customers),
            unitOfWork,
            bus,
            new FixedCorrelationContext(),
            CancellationToken.None
        );

    private sealed class BulkCustomers(IReadOnlyList<DomainCustomer> customers) : ICustomerRepository
    {
        public Task<DomainCustomer?> GetByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(customers.FirstOrDefault(c => c.Id == id));

        public Task<IReadOnlyList<DomainCustomer>> GetByIdsAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> ids,
            CancellationToken ct
        ) =>
            Task.FromResult<IReadOnlyList<DomainCustomer>>(
                customers.Where(c => c.TenantId == tenantId && ids.Contains(c.Id)).ToList()
            );

        public Task<Guid?> FindCustomerIdByFiscalBlindIndexAsync(Guid t, string b, Guid? e, CancellationToken ct) =>
            Task.FromResult<Guid?>(null);

        public Task<Guid?> FindRelationIdByFiscalBlindIndexAsync(Guid t, string b, Guid? e, CancellationToken ct) =>
            Task.FromResult<Guid?>(null);

        public Task<IReadOnlyList<DomainCustomer>> ListByAssignedPreparerAsync(
            Guid t,
            Guid p,
            int b,
            Guid a,
            CancellationToken ct
        ) => Task.FromResult<IReadOnlyList<DomainCustomer>>([]);

        public Task AddAsync(DomainCustomer customer, CancellationToken ct) => Task.CompletedTask;
    }
}
