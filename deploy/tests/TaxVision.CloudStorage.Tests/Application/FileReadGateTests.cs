using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Application;

public sealed class FileReadGateTests
{
    [Theory]
    [InlineData(OwnerType.Invoice, FileReadSurface.Billing)]
    [InlineData(OwnerType.Signature, FileReadSurface.Signature)]
    public void Owners_with_their_own_module_need_its_read_permission(OwnerType owner, FileReadSurface surface)
    {
        Assert.Equal(surface, FileReadGate.SurfaceOf(owner));
    }

    [Theory]
    [InlineData(OwnerType.Customer)]
    [InlineData(OwnerType.Tenant)]
    [InlineData(OwnerType.User)]
    [InlineData(OwnerType.Communication)]
    public void Other_owners_keep_todays_rule(OwnerType owner)
    {
        Assert.Null(FileReadGate.SurfaceOf(owner));
    }

    [Fact]
    public void Billing_files_need_invoicing_view()
    {
        Assert.Equal(["invoicing.view"], FileReadGate.PermissionsOf(FileReadSurface.Billing));
    }

    [Fact]
    public void Signature_files_open_with_any_signature_screen_permission()
    {
        Assert.Equal(
            ["signature.request.read", "signature.preparer.manage", "signature.settings.manage"],
            FileReadGate.PermissionsOf(FileReadSurface.Signature)
        );
    }
}
