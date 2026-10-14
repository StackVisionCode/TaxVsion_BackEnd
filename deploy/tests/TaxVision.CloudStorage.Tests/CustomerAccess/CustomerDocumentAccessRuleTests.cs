using TaxVision.CloudStorage.Domain.CustomerAccess;

namespace TaxVision.CloudStorage.Tests.CustomerAccess;

public sealed class CustomerDocumentAccessRuleTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();
    private static readonly Guid Resource = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public void Create_rejects_empty_identifiers()
    {
        var result = CustomerDocumentAccessRule.Create(
            Guid.NewGuid(),
            Guid.Empty,
            Customer,
            AccessResourceType.File,
            Resource,
            AccessCapability.Download,
            Actor,
            DateTime.UtcNow,
            null
        );

        Assert.True(result.IsFailure);
        Assert.Equal(CustomerAccessErrors.InvalidIdentifiers, result.Error);
    }

    [Fact]
    public void Create_rejects_reason_longer_than_500_chars()
    {
        var result = CustomerDocumentAccessRule.Create(
            Guid.NewGuid(),
            Tenant,
            Customer,
            AccessResourceType.Folder,
            Resource,
            AccessCapability.Download,
            Actor,
            DateTime.UtcNow,
            new string('x', 501)
        );

        Assert.True(result.IsFailure);
        Assert.Equal(CustomerAccessErrors.InvalidReason, result.Error);
    }

    [Fact]
    public void Create_trims_and_nulls_blank_reason()
    {
        var result = CustomerDocumentAccessRule.Create(
            Guid.NewGuid(),
            Tenant,
            Customer,
            AccessResourceType.File,
            Resource,
            AccessCapability.Download,
            Actor,
            DateTime.UtcNow,
            "   "
        );

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Reason);
    }

    [Fact]
    public void Create_trims_trailing_whitespace()
    {
        var result = CustomerDocumentAccessRule.Create(
            Guid.NewGuid(),
            Tenant,
            Customer,
            AccessResourceType.File,
            Resource,
            AccessCapability.Download,
            Actor,
            DateTime.UtcNow,
            "  blocked by compliance  "
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("blocked by compliance", result.Value.Reason);
    }

    [Fact]
    public void Refresh_updates_actor_and_timestamp()
    {
        var created = CustomerDocumentAccessRule
            .Create(
                Guid.NewGuid(),
                Tenant,
                Customer,
                AccessResourceType.File,
                Resource,
                AccessCapability.Download,
                Actor,
                new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
                "first"
            )
            .Value;

        var newActor = Guid.NewGuid();
        var newNow = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc);
        var refreshed = created.Refresh(newActor, newNow, "second");

        Assert.True(refreshed.IsSuccess);
        Assert.Equal(newActor, created.SetByUserId);
        Assert.Equal(newNow, created.SetAtUtc);
        Assert.Equal("second", created.Reason);
    }
}
