using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Infrastructure;

public sealed class UserConfigurationTests
{
    [Fact]
    public void The_email_is_unique_per_office_within_each_account_kind()
    {
        using var db = new AuthDbContext(
            new DbContextOptionsBuilder<AuthDbContext>().UseSqlServer("Server=unused;Database=unused").Options,
            new FakeMessageBus(),
            new NoTenant()
        );

        var emailIndexes = db
            .Model.FindEntityType(typeof(User))!
            .GetIndexes()
            .Where(index => index.Properties.Select(p => p.Name).SequenceEqual(["TenantId", "Email"]))
            .ToList();

        Assert.Equal(2, emailIndexes.Count);
        Assert.All(emailIndexes, index => Assert.True(index.IsUnique));
        Assert.Contains(emailIndexes, index => index.GetFilter() == "[ActorType] = N'CustomerPortal'");
        Assert.Contains(emailIndexes, index => index.GetFilter() == "[ActorType] <> N'CustomerPortal'");
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;

        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
