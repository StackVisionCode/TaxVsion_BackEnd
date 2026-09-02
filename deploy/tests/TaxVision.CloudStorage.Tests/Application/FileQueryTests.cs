using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Files.Queries;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>
/// ListFilesHandler — filtro plano por dueno para staff (ownerType/ownerId) y su bloqueo
/// para el portal de cliente, cuyo alcance queda forzado a su propio customerId.
/// </summary>
public sealed class FileQueryTests
{
    private static readonly StorageActorScope StaffScope = new(false, null);

    private static FileObject File(Guid tenantId, OwnerType ownerType, Guid? ownerId)
    {
        var key = ObjectKey.Create($"tenants/{tenantId:N}/{ownerType}/documents/2025/{Guid.NewGuid():N}.pdf").Value;
        return FileObject
            .Register(
                Guid.NewGuid(),
                tenantId,
                ownerType,
                ownerId,
                FolderType.Documents,
                2025,
                key,
                "doc.pdf",
                "application/pdf",
                10,
                Guid.NewGuid(),
                DateTime.UtcNow,
                DateTime.UtcNow.AddHours(24)
            )
            .Value;
    }

    [Fact]
    public async Task Staff_filtering_by_owner_returns_only_that_owners_files()
    {
        var tenantId = Guid.NewGuid();
        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        var files = new FakeFileObjectRepository();
        files.Seed(File(tenantId, OwnerType.Customer, customerA));
        files.Seed(File(tenantId, OwnerType.Customer, customerA));
        files.Seed(File(tenantId, OwnerType.Customer, customerB));
        files.Seed(File(tenantId, OwnerType.Tenant, null));

        var result = await ListFilesHandler.Handle(
            new ListFilesQuery(tenantId, StaffScope, OwnerType.Customer, customerA, 0, 50),
            files,
            CancellationToken.None
        );

        Assert.Equal(2, result.Count);
        Assert.All(result, file => Assert.Equal(customerA, file.OwnerId));
    }

    [Fact]
    public async Task Staff_without_owner_filter_returns_all_owners()
    {
        var tenantId = Guid.NewGuid();
        var files = new FakeFileObjectRepository();
        files.Seed(File(tenantId, OwnerType.Customer, Guid.NewGuid()));
        files.Seed(File(tenantId, OwnerType.Tenant, null));

        var result = await ListFilesHandler.Handle(
            new ListFilesQuery(tenantId, StaffScope, null, null, 0, 50),
            files,
            CancellationToken.None
        );

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task Customer_portal_ignores_owner_filter_and_sees_only_its_own_files()
    {
        var tenantId = Guid.NewGuid();
        var ownCustomer = Guid.NewGuid();
        var otherCustomer = Guid.NewGuid();
        var portalScope = new StorageActorScope(true, ownCustomer);
        var files = new FakeFileObjectRepository();
        files.Seed(File(tenantId, OwnerType.Customer, ownCustomer));
        files.Seed(File(tenantId, OwnerType.Customer, otherCustomer));

        // Aun pasando el ownerId de OTRO customer, el portal solo puede ver el suyo.
        var result = await ListFilesHandler.Handle(
            new ListFilesQuery(tenantId, portalScope, OwnerType.Customer, otherCustomer, 0, 50),
            files,
            CancellationToken.None
        );

        Assert.Single(result);
        Assert.Equal(ownCustomer, Assert.Single(result).OwnerId);
    }
}
