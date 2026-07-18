using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Backfill;
using TaxVision.Correspondence.Domain.Backfill;
using TaxVision.Correspondence.Domain.Projections;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Tests.Projections;

namespace TaxVision.Correspondence.Tests.Backfill;

public sealed class TenantCustomerBackfillServiceTests
{
    [Fact]
    public async Task EnsureBackfilledAsync_seeds_every_active_customer_and_marks_the_tenant_done()
    {
        var tenantId = Guid.NewGuid();
        var customer1 = Guid.NewGuid();
        var customer2 = Guid.NewGuid();
        var client = new FakeCorrespondenceCustomerClient();
        client.Seed(
            tenantId,
            new RemoteCustomerSummary(customer1, "customer1@example.com", true),
            new RemoteCustomerSummary(customer2, "Customer2@Example.com", true)
        );
        var emailRepository = new FakeCustomerEmailAddressRepository();
        var stateRepository = new FakeTenantBackfillStateRepository();
        var unitOfWork = new FakeUnitOfWork();
        var sut = CreateSut(stateRepository, emailRepository, client, unitOfWork);

        await sut.EnsureBackfilledAsync(tenantId, CancellationToken.None);

        Assert.Equal(2, emailRepository.All.Count);
        Assert.Contains(
            emailRepository.All,
            x => x.CustomerId == customer1 && x.EmailAddress == "customer1@example.com"
        );
        Assert.Contains(
            emailRepository.All,
            x => x.CustomerId == customer2 && x.EmailAddress == "customer2@example.com"
        );
        Assert.NotNull(await stateRepository.GetByTenantIdAsync(tenantId));
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task EnsureBackfilledAsync_is_a_no_op_when_the_tenant_was_already_backfilled()
    {
        var tenantId = Guid.NewGuid();
        var client = new FakeCorrespondenceCustomerClient();
        client.Seed(tenantId, new RemoteCustomerSummary(Guid.NewGuid(), "late@example.com", true));
        var stateRepository = new FakeTenantBackfillStateRepository();
        await stateRepository.AddAsync(TenantBackfillState.Create(tenantId));
        var emailRepository = new FakeCustomerEmailAddressRepository();
        var sut = CreateSut(stateRepository, emailRepository, client, new FakeUnitOfWork());

        await sut.EnsureBackfilledAsync(tenantId, CancellationToken.None);

        Assert.Empty(emailRepository.All);
        Assert.Empty(client.RequestedPages);
    }

    [Fact]
    public async Task EnsureBackfilledAsync_does_not_mark_the_tenant_done_when_a_page_fails()
    {
        var tenantId = Guid.NewGuid();
        var client = new FakeCorrespondenceCustomerClient();
        client.FailNextCall();
        var stateRepository = new FakeTenantBackfillStateRepository();
        var unitOfWork = new FakeUnitOfWork();
        var sut = CreateSut(stateRepository, new FakeCustomerEmailAddressRepository(), client, unitOfWork);

        await sut.EnsureBackfilledAsync(tenantId, CancellationToken.None);

        Assert.Null(await stateRepository.GetByTenantIdAsync(tenantId));
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task EnsureBackfilledAsync_skips_a_customer_that_already_has_a_row_and_one_with_an_invalid_email()
    {
        var tenantId = Guid.NewGuid();
        var existingCustomer = Guid.NewGuid();
        var invalidEmailCustomer = Guid.NewGuid();
        var client = new FakeCorrespondenceCustomerClient();
        client.Seed(
            tenantId,
            new RemoteCustomerSummary(existingCustomer, "already@example.com", true),
            new RemoteCustomerSummary(invalidEmailCustomer, "not-an-email", true)
        );
        var emailRepository = new FakeCustomerEmailAddressRepository();
        emailRepository.Seed(
            CustomerEmailAddress.Create(tenantId, existingCustomer, EmailAddress.Create("already@example.com").Value)
        );
        var stateRepository = new FakeTenantBackfillStateRepository();
        var sut = CreateSut(stateRepository, emailRepository, client, new FakeUnitOfWork());

        await sut.EnsureBackfilledAsync(tenantId, CancellationToken.None);

        Assert.Single(emailRepository.All);
        Assert.NotNull(await stateRepository.GetByTenantIdAsync(tenantId));
    }

    private static TenantCustomerBackfillService CreateSut(
        ITenantBackfillStateRepository stateRepository,
        ICustomerEmailAddressRepository emailRepository,
        ICorrespondenceCustomerClient client,
        FakeUnitOfWork unitOfWork
    ) => new(stateRepository, emailRepository, client, unitOfWork, NullLogger<TenantCustomerBackfillService>.Instance);
}
