using BuildingBlocks.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Backfill;
using TaxVision.Correspondence.Application.Ingest;
using TaxVision.Correspondence.Domain.Backfill;
using TaxVision.Correspondence.Domain.Projections;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Tests.Ingest;
using TaxVision.Correspondence.Tests.Projections;

namespace TaxVision.Correspondence.Tests.Backfill;

public sealed class BackfillTenantCustomersHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly FakeTenantBackfillStateRepository _states = new();
    private readonly FakeCustomerEmailAddressRepository _emails = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeMessageBus _bus = new();

    private Task HandleAsync(FakeCorrespondenceCustomerClient client) =>
        BackfillTenantCustomersHandler.Handle(
            new BackfillTenantCustomersCommand(_tenantId),
            _states,
            _emails,
            client,
            _unitOfWork,
            _bus,
            NullLogger<BackfillTenantCustomersCommand>.Instance,
            CancellationToken.None
        );

    private static RemoteCustomerSummary Customer(int index) =>
        new(Guid.NewGuid(), $"customer{index}@example.com", true);

    [Fact]
    public async Task Handle_ResumesFromLastConfirmedPage()
    {
        var client = new FakeCorrespondenceCustomerClient(pageSize: 1);
        client.Seed(_tenantId, Customer(1), Customer(2), Customer(3), Customer(4), Customer(5));
        var state = TenantBackfillState.Start(_tenantId);
        state.ConfirmPage(3, isLastPage: false);
        await _states.AddAsync(state);

        await HandleAsync(client);

        Assert.Equal([4], client.RequestedPageNumbers);
        Assert.Equal("customer4@example.com", Assert.Single(_emails.All).EmailAddress);
        Assert.Equal(4, state.LastConfirmedPage);
        Assert.False(state.IsCompleted);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
        Assert.Equal(_tenantId, Assert.Single(_bus.Published.OfType<BackfillTenantCustomersCommand>()).TenantId);
        Assert.Equal("customer4@example.com", Assert.Single(_bus.Published.OfType<LinkUnlinkedMailCommand>()).Address);
    }

    [Fact]
    public async Task Handle_LastPage_MarksTheTenantDoneAndEnqueuesNothing()
    {
        var client = new FakeCorrespondenceCustomerClient();
        client.Seed(_tenantId, Customer(1), new RemoteCustomerSummary(Guid.NewGuid(), "Customer2@Example.com", true));

        await HandleAsync(client);

        Assert.Equal(2, _emails.All.Count);
        Assert.Contains(_emails.All, x => x.EmailAddress == "customer2@example.com");
        Assert.True((await _states.GetByTenantIdAsync(_tenantId))!.IsCompleted);
        Assert.Empty(_bus.Published.OfType<BackfillTenantCustomersCommand>());
    }

    [Fact]
    public async Task Handle_CompletedTenant_IsANoOp()
    {
        var client = new FakeCorrespondenceCustomerClient();
        client.Seed(_tenantId, Customer(1));
        var state = TenantBackfillState.Start(_tenantId);
        state.ConfirmPage(1, isLastPage: true);
        await _states.AddAsync(state);

        await HandleAsync(client);

        Assert.Empty(_emails.All);
        Assert.Empty(client.RequestedPages);
    }

    [Fact]
    public async Task Handle_PageFails_ThrowsForRetryAndSavesNothing()
    {
        var client = new FakeCorrespondenceCustomerClient();
        client.FailNextCall();

        await Assert.ThrowsAsync<DependencyNotReadyException>(() => HandleAsync(client));

        Assert.Null(await _states.GetByTenantIdAsync(_tenantId));
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_SkipsExistingRowInvalidEmailAndSharedEmailInThePage()
    {
        var existing = Guid.NewGuid();
        var client = new FakeCorrespondenceCustomerClient();
        client.Seed(
            _tenantId,
            new RemoteCustomerSummary(existing, "already@example.com", true),
            new RemoteCustomerSummary(Guid.NewGuid(), "not-an-email", true),
            new RemoteCustomerSummary(Guid.NewGuid(), "shared@example.com", true),
            new RemoteCustomerSummary(Guid.NewGuid(), "shared@example.com", true)
        );
        _emails.Seed(
            CustomerEmailAddress.Create(_tenantId, existing, EmailAddress.Create("already@example.com").Value)
        );

        await HandleAsync(client);

        Assert.Equal(2, _emails.All.Count);
        Assert.True((await _states.GetByTenantIdAsync(_tenantId))!.IsCompleted);
    }
}
