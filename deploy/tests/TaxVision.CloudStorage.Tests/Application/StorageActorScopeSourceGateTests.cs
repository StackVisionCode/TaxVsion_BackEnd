using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Application;

public sealed class StorageActorScopeSourceGateTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void Staff_without_billing_cannot_reach_an_invoice_file()
    {
        var scope = Staff(readable: []);

        Assert.False(scope.CanAccess(File(OwnerType.Invoice)));
    }

    [Fact]
    public void Staff_with_billing_reaches_an_invoice_file()
    {
        var scope = Staff(readable: [FileReadSurface.Billing]);

        Assert.True(scope.CanAccess(File(OwnerType.Invoice)));
        Assert.False(scope.CanAccess(File(OwnerType.Signature)));
    }

    [Fact]
    public void Files_of_other_owners_ignore_the_gate()
    {
        var scope = Staff(readable: []);

        Assert.True(scope.CanAccess(File(OwnerType.Customer, Guid.NewGuid())));
        Assert.True(scope.CanAccess(File(OwnerType.Communication)));
    }

    [Fact]
    public void Without_readable_surfaces_the_gate_is_off()
    {
        var scope = new StorageActorScope(false, null);

        Assert.True(scope.CanAccess(File(OwnerType.Invoice)));
    }

    [Fact]
    public void Service_tokens_are_never_gated()
    {
        var scope = new StorageActorScope(false, null)
        {
            IsService = true,
            ReadableSurfaces = new HashSet<FileReadSurface>(),
        };

        Assert.True(scope.CanAccess(File(OwnerType.Invoice)));
    }

    private static StorageActorScope Staff(FileReadSurface[] readable) =>
        new(false, null) { TenantId = Tenant, ReadableSurfaces = readable.ToHashSet() };

    internal static FileObject File(OwnerType ownerType, Guid? ownerId = null)
    {
        var key = ObjectKey.Create($"tenants/{Tenant:N}/{ownerType}/documents/2025/{Guid.NewGuid():N}.pdf").Value;
        return FileObject
            .Register(
                Guid.NewGuid(),
                Tenant,
                ownerType,
                ownerId ?? Guid.NewGuid(),
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
}
