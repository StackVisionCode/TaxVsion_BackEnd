using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Domain;

// Foto de perfil del cliente: pendiente mientras corre el antivirus, vigente al confirmarse.
public sealed class CustomerProfilePictureTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Begin_leaves_the_picture_pending_without_changing_the_current_one()
    {
        var customer = NewCustomer();
        var fileId = Guid.NewGuid();

        var result = customer.BeginProfilePictureChange(fileId, Actor, Now);

        Assert.True(result.IsSuccess);
        Assert.Null(customer.ProfilePictureFileId);
        Assert.Equal(fileId, customer.PendingProfilePictureFileId);
        Assert.Equal(Now, customer.PendingProfilePictureRequestedAtUtc);
        Assert.Equal(Actor, customer.LastModifiedByUserId);
    }

    [Fact]
    public void Begin_rejects_an_empty_file_id()
    {
        var result = NewCustomer().BeginProfilePictureChange(Guid.Empty, Actor, Now);

        Assert.Equal("Customer.ProfilePicture.FileId", result.Error.Code);
    }

    [Fact]
    public void Archived_customer_cannot_begin_nor_remove_a_picture()
    {
        var customer = NewCustomer();
        customer.Archive(Actor);

        Assert.Equal("Customer.Archived", customer.BeginProfilePictureChange(Guid.NewGuid(), Actor, Now).Error.Code);
        Assert.Equal("Customer.Archived", customer.RemoveProfilePicture(Actor).Error.Code);
    }

    [Fact]
    public void Confirm_promotes_the_pending_picture_and_returns_the_previous_one()
    {
        var customer = WithPicture(out var first);
        var second = Guid.NewGuid();
        customer.BeginProfilePictureChange(second, Actor, Now);

        var result = customer.ConfirmProfilePicture(second);

        Assert.True(result.IsSuccess);
        Assert.Equal(first, result.Value);
        Assert.Equal(second, customer.ProfilePictureFileId);
        Assert.Null(customer.PendingProfilePictureFileId);
        Assert.Null(customer.PendingProfilePictureRequestedAtUtc);
    }

    [Fact]
    public void Confirm_of_a_file_that_is_not_pending_is_stale_and_changes_nothing()
    {
        var customer = WithPicture(out var current);
        var pending = Guid.NewGuid();
        customer.BeginProfilePictureChange(pending, Actor, Now);

        var result = customer.ConfirmProfilePicture(Guid.NewGuid());

        Assert.Equal("Customer.ProfilePicture.Stale", result.Error.Code);
        Assert.Equal(current, customer.ProfilePictureFileId);
        Assert.Equal(pending, customer.PendingProfilePictureFileId);
    }

    [Fact]
    public void A_newer_upload_replaces_the_pending_one_so_the_older_result_is_stale()
    {
        var customer = NewCustomer();
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();
        customer.BeginProfilePictureChange(older, Actor, Now);
        customer.BeginProfilePictureChange(newer, Actor, Now.AddSeconds(5));

        Assert.True(customer.ConfirmProfilePicture(older).IsFailure);
        Assert.True(customer.ConfirmProfilePicture(newer).IsSuccess);
        Assert.Equal(newer, customer.ProfilePictureFileId);
    }

    [Fact]
    public void Discard_only_clears_the_matching_pending_picture()
    {
        var customer = WithPicture(out var current);
        var pending = Guid.NewGuid();
        customer.BeginProfilePictureChange(pending, Actor, Now);

        Assert.False(customer.DiscardPendingProfilePicture(Guid.NewGuid()));
        Assert.Equal(pending, customer.PendingProfilePictureFileId);

        Assert.True(customer.DiscardPendingProfilePicture(pending));
        Assert.Null(customer.PendingProfilePictureFileId);
        Assert.Equal(current, customer.ProfilePictureFileId);
    }

    [Fact]
    public void Remove_clears_current_and_pending_and_returns_the_current_one()
    {
        var customer = WithPicture(out var current);
        customer.BeginProfilePictureChange(Guid.NewGuid(), Actor, Now);

        var result = customer.RemoveProfilePicture(Actor);

        Assert.Equal(current, result.Value);
        Assert.Null(customer.ProfilePictureFileId);
        Assert.Null(customer.PendingProfilePictureFileId);
    }

    [Fact]
    public void Remove_without_picture_is_a_no_op()
    {
        var customer = NewCustomer();
        var before = customer.UpdatedAtUtc;

        var result = customer.RemoveProfilePicture(Actor);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(before, customer.UpdatedAtUtc);
    }

    [Fact]
    public void Archive_clears_the_picture_and_a_late_confirmation_is_stale()
    {
        var customer = WithPicture(out _);
        var pending = Guid.NewGuid();
        customer.BeginProfilePictureChange(pending, Actor, Now);

        customer.Archive(Actor);

        Assert.Null(customer.ProfilePictureFileId);
        Assert.Null(customer.PendingProfilePictureFileId);
        Assert.True(customer.ConfirmProfilePicture(pending).IsFailure);
    }

    [Fact]
    public void Pending_picture_expires_after_the_timeout()
    {
        var customer = NewCustomer();
        customer.BeginProfilePictureChange(Guid.NewGuid(), Actor, Now);

        Assert.False(customer.IsPendingProfilePictureExpired(Now.AddMinutes(9)));
        Assert.True(customer.IsPendingProfilePictureExpired(Now + DomainCustomer.PendingProfilePictureTimeout));
    }

    private static DomainCustomer WithPicture(out Guid fileId)
    {
        var customer = NewCustomer();
        fileId = Guid.NewGuid();
        customer.BeginProfilePictureChange(fileId, Actor, Now);
        customer.ConfirmProfilePicture(fileId);
        return customer;
    }

    private static DomainCustomer NewCustomer() =>
        DomainCustomer
            .Register(
                TenantId,
                CustomerKind.Individual,
                PersonalName.Create("Ana", "Client").Value,
                null,
                EmailAddress.Create("ana@example.com").Value,
                null,
                Language.En,
                Actor
            )
            .Value;
}
