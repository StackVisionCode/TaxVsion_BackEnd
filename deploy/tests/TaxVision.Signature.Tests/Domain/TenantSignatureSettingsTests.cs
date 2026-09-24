using TaxVision.Signature.Domain.Settings;
using Xunit;

namespace TaxVision.Signature.Tests.Domain;

public sealed class TenantSignatureSettingsTests
{
    private static TenantSignatureSettings New() =>
        TenantSignatureSettings.CreateForNewTenant(Guid.NewGuid(), "encrypted-secret").Value;

    [Fact]
    public void New_tenant_allows_employee_own_signature_by_default()
    {
        var settings = New();

        Assert.True(settings.AllowEmployeeOwnSignature);
    }

    [Fact]
    public void Disable_forces_office_signature()
    {
        var settings = New();

        settings.DisableEmployeeOwnSignature();

        Assert.False(settings.AllowEmployeeOwnSignature);
    }

    [Fact]
    public void Enable_after_disable_restores_own_signature()
    {
        var settings = New();
        settings.DisableEmployeeOwnSignature();

        settings.EnableEmployeeOwnSignature();

        Assert.True(settings.AllowEmployeeOwnSignature);
    }
}
