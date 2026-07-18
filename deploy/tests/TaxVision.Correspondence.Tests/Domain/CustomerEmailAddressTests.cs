using TaxVision.Correspondence.Domain.Projections;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Tests.Domain;

public sealed class CustomerEmailAddressTests
{
    [Fact]
    public void Create_stores_normalized_email_as_primary_customer_source()
    {
        var email = EmailAddress.Create("John.Doe@Example.com").Value;

        var entity = CustomerEmailAddress.Create(Guid.NewGuid(), Guid.NewGuid(), email);

        Assert.Equal("john.doe@example.com", entity.EmailAddress);
        Assert.True(entity.IsPrimary);
        Assert.Equal(CustomerEmailSource.CustomerPrimary, entity.Source);
        Assert.True(entity.IsActive);
        Assert.Null(entity.DeletedAtUtc);
    }

    [Fact]
    public void Create_rejects_empty_tenant_or_customer_id()
    {
        var email = EmailAddress.Create("a@example.com").Value;

        Assert.Throws<ArgumentException>(() => CustomerEmailAddress.Create(Guid.Empty, Guid.NewGuid(), email));
        Assert.Throws<ArgumentException>(() => CustomerEmailAddress.Create(Guid.NewGuid(), Guid.Empty, email));
    }

    [Fact]
    public void UpdateEmail_changes_the_value_and_bumps_UpdatedAtUtc()
    {
        var entity = CustomerEmailAddress.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            EmailAddress.Create("old@example.com").Value
        );
        var before = entity.UpdatedAtUtc;
        Thread.Sleep(10);

        entity.UpdateEmail(EmailAddress.Create("New@Example.com").Value);

        Assert.Equal("new@example.com", entity.EmailAddress);
        Assert.True(entity.UpdatedAtUtc > before);
    }

    [Fact]
    public void UpdateEmail_is_a_noop_when_the_email_did_not_change()
    {
        var entity = CustomerEmailAddress.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            EmailAddress.Create("same@example.com").Value
        );
        var before = entity.UpdatedAtUtc;
        Thread.Sleep(10);

        entity.UpdateEmail(EmailAddress.Create("Same@Example.com").Value);

        Assert.Equal(before, entity.UpdatedAtUtc);
    }

    [Fact]
    public void SoftDelete_sets_DeletedAtUtc_and_is_idempotent()
    {
        var entity = CustomerEmailAddress.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            EmailAddress.Create("a@example.com").Value
        );

        entity.SoftDelete();
        var firstDeletedAt = entity.DeletedAtUtc;
        Assert.False(entity.IsActive);

        entity.SoftDelete();

        Assert.Equal(firstDeletedAt, entity.DeletedAtUtc);
    }

    [Fact]
    public void Reactivate_clears_DeletedAtUtc_and_optionally_updates_email()
    {
        var entity = CustomerEmailAddress.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            EmailAddress.Create("old@example.com").Value
        );
        entity.SoftDelete();

        entity.Reactivate(EmailAddress.Create("updated@example.com").Value);

        Assert.True(entity.IsActive);
        Assert.Equal("updated@example.com", entity.EmailAddress);
    }

    [Fact]
    public void Reactivate_without_a_new_email_keeps_the_existing_one()
    {
        var entity = CustomerEmailAddress.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            EmailAddress.Create("keep@example.com").Value
        );
        entity.SoftDelete();

        entity.Reactivate();

        Assert.True(entity.IsActive);
        Assert.Equal("keep@example.com", entity.EmailAddress);
    }
}
