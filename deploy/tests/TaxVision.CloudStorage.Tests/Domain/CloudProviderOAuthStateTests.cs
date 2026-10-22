using TaxVision.CloudStorage.Domain.Providers;

namespace TaxVision.CloudStorage.Tests.Domain;

public sealed class CloudProviderOAuthStateTests
{
    [Fact]
    public void The_state_sent_to_the_provider_is_random_not_the_tenant_id()
    {
        var tenantId = Guid.NewGuid();

        var first = CloudProviderOAuthState.Create(tenantId, Guid.NewGuid(), "dropbox", null);
        var second = CloudProviderOAuthState.Create(tenantId, Guid.NewGuid(), "dropbox", null);

        Assert.NotEqual(tenantId, first.Id);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(tenantId, first.TenantId);
    }

    [Fact]
    public void A_state_only_completes_the_provider_that_issued_it_before_it_expires()
    {
        var state = CloudProviderOAuthState.Create(Guid.NewGuid(), Guid.NewGuid(), "google-drive", null);

        Assert.True(state.CanComplete("google-drive", DateTime.UtcNow));
        Assert.False(state.CanComplete("dropbox", DateTime.UtcNow));
        Assert.False(state.CanComplete("google-drive", DateTime.UtcNow.AddMinutes(11)));
    }

    [Theory]
    [InlineData("https://acme.taxoffix.io", "https://acme.taxoffix.io")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Keeps_the_crm_origin_to_return_to(string? origin, string? expected)
    {
        var state = CloudProviderOAuthState.Create(Guid.NewGuid(), Guid.NewGuid(), "onedrive", origin);

        Assert.Equal(expected, state.ReturnOrigin);
    }

    [Fact]
    public void Drops_an_origin_longer_than_the_column()
    {
        var origin = "https://" + new string('a', CloudProviderOAuthState.ReturnOriginMaxLength) + ".taxoffix.io";

        Assert.Null(CloudProviderOAuthState.Create(Guid.NewGuid(), Guid.NewGuid(), "onedrive", origin).ReturnOrigin);
    }

    [Fact]
    public void Each_provider_connection_of_a_tenant_gets_its_own_key()
    {
        var tenantId = Guid.NewGuid();

        var drive = TenantCloudConnection.Connect(tenantId, "google-drive", "a@b.com", "x");
        var dropbox = TenantCloudConnection.Connect(tenantId, "dropbox", "a@b.com", "y");

        Assert.NotEqual(drive.Id, dropbox.Id);
        Assert.NotEqual(tenantId, drive.Id);
    }
}
