using TaxVision.Signature.Application.Sealing;

namespace TaxVision.Signature.Tests.Sealing;

/// <summary>
/// Rol con que se declara un sellado o un certificado: un cliente o ninguno → el rol base (con su cliente si lo
/// hay); varios → el maestro, sin cliente (cada cliente recibe su copia).
/// </summary>
public sealed class SealedFileRoleTests
{
    [Fact]
    public void A_single_customer_keeps_the_base_role_and_the_customer()
    {
        var customerId = Guid.NewGuid();

        var (role, relatedCustomerId) = SealedFileRole.Resolve([customerId, null, customerId], SealedFileRole.Sealed);

        Assert.Equal("sealed", role);
        Assert.Equal(customerId, relatedCustomerId);
    }

    [Fact]
    public void Without_customers_the_base_role_has_no_customer()
    {
        var (role, relatedCustomerId) = SealedFileRole.Resolve([null, null], SealedFileRole.Certificate);

        Assert.Equal("certificate", role);
        Assert.Null(relatedCustomerId);
    }

    [Theory]
    [InlineData("original")]
    [InlineData("partial_copy")]
    public void Several_customers_keep_other_roles_without_customer(string baseRole)
    {
        var (role, relatedCustomerId) = SealedFileRole.Resolve([Guid.NewGuid(), Guid.NewGuid()], baseRole);

        Assert.Equal(baseRole, role);
        Assert.Null(relatedCustomerId);
    }

    [Theory]
    [InlineData("sealed", "sealed_master")]
    [InlineData("certificate", "certificate_master")]
    public void Several_customers_make_the_file_a_master_without_customer(string baseRole, string expected)
    {
        var (role, relatedCustomerId) = SealedFileRole.Resolve([Guid.NewGuid(), Guid.NewGuid()], baseRole);

        Assert.Equal(expected, role);
        Assert.Null(relatedCustomerId);
    }
}
