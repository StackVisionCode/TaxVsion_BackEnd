using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Infrastructure.Persistence;
using TaxVision.Customer.Tests.Application;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Persistence;

// La foto viaja en la lista, el detalle y la reconciliación; el detalle además informa la subida en curso.
public sealed class CustomerReadServiceAvatarTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _database = Guid.NewGuid().ToString();

    [Fact]
    public async Task Search_and_overview_return_the_current_picture()
    {
        var (withPhoto, photo) = await SeedWithPictureAsync();
        await SeedAsync(NewCustomer("bare"));

        await using var db = CreateContext();
        var reader = new CustomerReadService(db, new NoopProtector());

        var page = await reader.SearchAsync(_tenantId, null, CustomerStatusFilter.All, 1, 20);
        Assert.Equal(photo, page.Items.Single(i => i.Id == withPhoto).AvatarFileId);
        Assert.Null(page.Items.Single(i => i.Id != withPhoto).AvatarFileId);

        var overview = await reader.GetOverviewAsync(_tenantId, 6);
        Assert.Equal(photo, overview.Recent.Single(i => i.Id == withPhoto).AvatarFileId);
    }

    [Fact]
    public async Task Reconciliation_returns_the_current_picture()
    {
        var (withPhoto, photo) = await SeedWithPictureAsync();

        await using var db = CreateContext();
        var page = await new CustomerReadService(db, new NoopProtector()).ListForReconciliationAsync(
            CustomerStatusFilter.All,
            1,
            200
        );

        Assert.Equal(photo, page.Items.Single(i => i.CustomerId == withPhoto).AvatarFileId);
    }

    [Fact]
    public async Task Detail_returns_the_picture_and_a_processing_upload()
    {
        var (customerId, photo) = await SeedWithPictureAsync(pendingRequestedAt: Now.AddMinutes(-2));

        var detail = await ReadDetailAsync(customerId);

        Assert.Equal(photo, detail.AvatarFileId);
        Assert.NotNull(detail.PendingAvatarFileId);
        Assert.Equal("processing", detail.AvatarUploadStatus);
    }

    [Fact]
    public async Task Detail_reports_an_expired_upload_as_failed()
    {
        var (customerId, _) = await SeedWithPictureAsync(pendingRequestedAt: Now.AddMinutes(-30));

        var detail = await ReadDetailAsync(customerId);

        Assert.Equal("failed", detail.AvatarUploadStatus);
    }

    private async Task<CustomerDetailResponse> ReadDetailAsync(Guid customerId)
    {
        await using var db = CreateContext();
        var reader = new CustomerReadService(db, new NoopProtector(), new FixedTimeProvider(Now));
        var detail = await reader.GetDetailByIdAsync(_tenantId, customerId);
        Assert.NotNull(detail);
        return detail!;
    }

    private async Task<(Guid CustomerId, Guid PhotoId)> SeedWithPictureAsync(DateTime? pendingRequestedAt = null)
    {
        var customer = NewCustomer("photo");
        var photo = Guid.NewGuid();
        customer.BeginProfilePictureChange(photo, Guid.NewGuid(), Now.AddDays(-1));
        customer.ConfirmProfilePicture(photo);
        if (pendingRequestedAt is { } requestedAt)
            customer.BeginProfilePictureChange(Guid.NewGuid(), Guid.NewGuid(), requestedAt);

        await SeedAsync(customer);
        return (customer.Id, photo);
    }

    private async Task SeedAsync(DomainCustomer customer)
    {
        await using var db = CreateContext();
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
    }

    private DomainCustomer NewCustomer(string name) =>
        DomainCustomer
            .Register(
                _tenantId,
                CustomerKind.Individual,
                PersonalName.Create("Ana", name).Value,
                null,
                EmailAddress.Create($"{name}-{Guid.NewGuid():N}@example.com").Value,
                null,
                Language.En,
                Guid.NewGuid()
            )
            .Value;

    private CustomerDbContext CreateContext()
    {
        var tenant = new FakeTenantContext();
        tenant.SetTenant(_tenantId);
        return new CustomerDbContext(
            new DbContextOptionsBuilder<CustomerDbContext>().UseInMemoryDatabase(_database).Options,
            tenant
        );
    }

    private sealed class FakeTenantContext : ITenantContext
    {
        private Guid? _tenantId;
        public Guid TenantId => _tenantId ?? throw new InvalidOperationException("TenantId is not set.");
        public bool HasTenant => _tenantId.HasValue;

        public void SetTenant(Guid tenantId) => _tenantId = tenantId;
    }

    private sealed class NoopProtector : ISensitiveDataProtector
    {
        public byte[] Protect(string plainText) => System.Text.Encoding.UTF8.GetBytes(plainText);

        public string Unprotect(byte[] cipher) => System.Text.Encoding.UTF8.GetString(cipher);

        public string ComputeBlindIndex(string plainText, Guid tenantId) => plainText;
    }
}
