using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Reconciliation;
using TaxVision.Correspondence.Domain.Projections;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Tests.Backfill;
using TaxVision.Correspondence.Tests.Projections;

namespace TaxVision.Correspondence.Tests.Reconciliation;

public sealed class CustomerEmailReconciliationServiceTests
{
    [Fact]
    public async Task ReconcileTenantAsync_creates_a_missing_projection()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var client = new FakeCorrespondenceCustomerClient();
        client.Seed(tenantId, new RemoteCustomerSummary(customerId, "new@example.com", true));
        var emailRepository = new FakeCustomerEmailAddressRepository();
        var unitOfWork = new FakeUnitOfWork();
        var sut = CreateSut(client, emailRepository, unitOfWork);

        var result = await sut.ReconcileTenantAsync(tenantId, CancellationToken.None);

        Assert.Equal(1, result.Created);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Reactivated);
        Assert.True(result.CompletedFully);
        Assert.Single(emailRepository.All, x => x.CustomerId == customerId && x.EmailAddress == "new@example.com");
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task ReconcileTenantAsync_updates_a_stale_email()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var client = new FakeCorrespondenceCustomerClient();
        client.Seed(tenantId, new RemoteCustomerSummary(customerId, "new-address@example.com", true));
        var emailRepository = new FakeCustomerEmailAddressRepository();
        emailRepository.Seed(
            CustomerEmailAddress.Create(tenantId, customerId, EmailAddress.Create("old-address@example.com").Value)
        );
        var sut = CreateSut(client, emailRepository, new FakeUnitOfWork());

        var result = await sut.ReconcileTenantAsync(tenantId, CancellationToken.None);

        Assert.Equal(1, result.Updated);
        Assert.Equal("new-address@example.com", emailRepository.All.Single().EmailAddress);
    }

    [Fact]
    public async Task ReconcileTenantAsync_reactivates_a_soft_deleted_projection_whose_customer_is_active_again()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var client = new FakeCorrespondenceCustomerClient();
        client.Seed(tenantId, new RemoteCustomerSummary(customerId, "resurfaced@example.com", true));
        var emailRepository = new FakeCustomerEmailAddressRepository();
        var deleted = CustomerEmailAddress.Create(
            tenantId,
            customerId,
            EmailAddress.Create("resurfaced@example.com").Value
        );
        deleted.SoftDelete();
        emailRepository.Seed(deleted);
        var sut = CreateSut(client, emailRepository, new FakeUnitOfWork());

        var result = await sut.ReconcileTenantAsync(tenantId, CancellationToken.None);

        Assert.Equal(1, result.Reactivated);
        Assert.True(emailRepository.All.Single().IsActive);
    }

    [Fact]
    public async Task ReconcileTenantAsync_is_a_no_op_when_everything_already_matches()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var client = new FakeCorrespondenceCustomerClient();
        client.Seed(tenantId, new RemoteCustomerSummary(customerId, "already-correct@example.com", true));
        var emailRepository = new FakeCustomerEmailAddressRepository();
        emailRepository.Seed(
            CustomerEmailAddress.Create(tenantId, customerId, EmailAddress.Create("already-correct@example.com").Value)
        );
        var unitOfWork = new FakeUnitOfWork();
        var sut = CreateSut(client, emailRepository, unitOfWork);

        var result = await sut.ReconcileTenantAsync(tenantId, CancellationToken.None);

        Assert.Equal(0, result.TotalFixed);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task ReconcileTenantAsync_reports_incomplete_when_a_page_fails()
    {
        var tenantId = Guid.NewGuid();
        var client = new FakeCorrespondenceCustomerClient();
        client.FailNextCall();
        var sut = CreateSut(client, new FakeCustomerEmailAddressRepository(), new FakeUnitOfWork());

        var result = await sut.ReconcileTenantAsync(tenantId, CancellationToken.None);

        Assert.False(result.CompletedFully);
        Assert.Equal(0, result.TotalFixed);
    }

    private static CustomerEmailReconciliationService CreateSut(
        ICorrespondenceCustomerClient client,
        ICustomerEmailAddressRepository emailRepository,
        FakeUnitOfWork unitOfWork
    ) => new(client, emailRepository, unitOfWork, NullLogger<CustomerEmailReconciliationService>.Instance);
}
