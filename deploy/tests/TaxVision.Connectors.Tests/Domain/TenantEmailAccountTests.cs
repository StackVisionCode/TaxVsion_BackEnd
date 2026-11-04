using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Tests.Domain;

public class TenantEmailAccountTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CreatedByUserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

    private static TenantEmailAccount CreateValidAccount() =>
        TenantEmailAccount
            .Create(TenantId, "office@gmail.com", ProviderCode.Gmail, CreatedByUserId, Now, "Office Inbox")
            .Value;

    [Fact]
    public void Create_WithValidData_Succeeds()
    {
        var result = TenantEmailAccount.Create(
            TenantId,
            "Office@Gmail.com",
            ProviderCode.Gmail,
            CreatedByUserId,
            Now,
            "Office Inbox"
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("office@gmail.com", result.Value.EmailAddress);
        Assert.Equal(TenantEmailAccountStatus.Draft, result.Value.Status);
        Assert.Equal(TenantId, result.Value.TenantId);
    }

    [Fact]
    public void Create_WithoutOwner_IsOfficeMailbox()
    {
        var account = TenantEmailAccount
            .Create(TenantId, "office@taxpro.com", ProviderCode.Gmail, CreatedByUserId, Now)
            .Value;

        Assert.True(account.IsOffice);
        Assert.Null(account.OwnerUserId);
    }

    [Fact]
    public void Create_WithOwner_IsPersonalMailbox()
    {
        var owner = Guid.NewGuid();
        var account = TenantEmailAccount
            .Create(TenantId, "me@taxpro.com", ProviderCode.Gmail, owner, Now, ownerUserId: owner)
            .Value;

        Assert.False(account.IsOffice);
        Assert.Equal(owner, account.OwnerUserId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("@missing-local.com")]
    [InlineData("missing-domain@")]
    public void Create_WithInvalidEmailAddress_Fails(string emailAddress)
    {
        var result = TenantEmailAccount.Create(TenantId, emailAddress, ProviderCode.Gmail, CreatedByUserId, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("TenantEmailAccount.EmailAddress", result.Error.Code);
    }

    [Fact]
    public void Create_WithEmptyTenantId_Fails()
    {
        var result = TenantEmailAccount.Create(
            Guid.Empty,
            "office@gmail.com",
            ProviderCode.Gmail,
            CreatedByUserId,
            Now
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TenantEmailAccount.Tenant", result.Error.Code);
    }

    [Fact]
    public void Create_WithEmptyCreatedByUserId_Fails()
    {
        var result = TenantEmailAccount.Create(TenantId, "office@gmail.com", ProviderCode.Gmail, Guid.Empty, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("TenantEmailAccount.CreatedByUserId", result.Error.Code);
    }

    [Fact]
    public void MarkConnected_FromDraft_Succeeds()
    {
        var account = CreateValidAccount();

        var result = account.MarkConnected(Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantEmailAccountStatus.Connected, account.Status);
        Assert.Equal(Now, account.ConnectedAtUtc);
    }

    [Fact]
    public void MarkConnected_FromActive_Fails()
    {
        var account = CreateValidAccount();
        account.MarkConnected(Now);
        account.Activate(Now);

        var result = account.MarkConnected(Now);

        Assert.True(result.IsFailure);
        Assert.Equal("TenantEmailAccount.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public void Activate_FromConnected_Succeeds()
    {
        var account = CreateValidAccount();
        account.MarkConnected(Now);

        var result = account.Activate(Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantEmailAccountStatus.Active, account.Status);
    }

    [Fact]
    public void Activate_FromDraft_Fails()
    {
        var account = CreateValidAccount();

        var result = account.Activate(Now);

        Assert.True(result.IsFailure);
        Assert.Equal("TenantEmailAccount.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public void Disconnect_FromActive_Succeeds()
    {
        var account = CreateValidAccount();
        account.MarkConnected(Now);
        account.Activate(Now);

        var result = account.Disconnect(Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantEmailAccountStatus.Disconnected, account.Status);
    }

    [Fact]
    public void Disconnect_Twice_FailsOnSecondCall()
    {
        var account = CreateValidAccount();
        account.Disconnect(Now);

        var result = account.Disconnect(Now);

        Assert.True(result.IsFailure);
        Assert.Equal("TenantEmailAccount.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public void MarkError_FromActive_SetsErrorStatus()
    {
        var account = CreateValidAccount();
        account.MarkConnected(Now);
        account.Activate(Now);

        account.MarkError(Now);

        Assert.Equal(TenantEmailAccountStatus.Error, account.Status);
    }

    [Fact]
    public void MarkConnected_FromError_Succeeds()
    {
        var account = CreateValidAccount();
        account.MarkError(Now);

        var result = account.MarkConnected(Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantEmailAccountStatus.Connected, account.Status);
    }

    [Fact]
    public void OffboardingReviewReason_OnlyForOfficeMailboxesLinkedToThePerson()
    {
        var office = CreateValidAccount();
        office.MarkConnected(Now);
        var personal = TenantEmailAccount
            .Create(TenantId, "jane@gmail.com", ProviderCode.Gmail, CreatedByUserId, Now, ownerUserId: CreatedByUserId)
            .Value;

        Assert.Equal(
            OfficeMailboxReviewReasons.AddressOfOffboardedUser,
            office.OffboardingReviewReasonFor(Guid.NewGuid(), " OFFICE@gmail.com ")
        );
        Assert.Equal(
            OfficeMailboxReviewReasons.ConnectedByOffboardedUser,
            office.OffboardingReviewReasonFor(CreatedByUserId, "other@x.com")
        );
        Assert.Null(office.OffboardingReviewReasonFor(Guid.NewGuid(), "other@x.com"));
        Assert.Null(personal.OffboardingReviewReasonFor(CreatedByUserId, "jane@gmail.com"));
    }

    [Fact]
    public void OffboardingReviewReason_IgnoresADisconnectedOfficeMailbox()
    {
        var office = CreateValidAccount();
        office.MarkConnected(Now);
        office.Disconnect(Now);

        Assert.Null(office.OffboardingReviewReasonFor(CreatedByUserId, "office@gmail.com"));
    }

    [Fact]
    public void RequireReview_IsIdempotent_AndDisconnectClearsIt()
    {
        var office = CreateValidAccount();
        office.MarkConnected(Now);

        Assert.True(office.RequireReview(OfficeMailboxReviewReasons.ConnectedByOffboardedUser, Now));
        Assert.False(office.RequireReview(OfficeMailboxReviewReasons.AddressOfOffboardedUser, Now.AddHours(1)));
        Assert.Equal(Now, office.ReviewRequiredAtUtc);
        Assert.Equal(OfficeMailboxReviewReasons.ConnectedByOffboardedUser, office.ReviewReason);

        office.Disconnect(Now);

        Assert.False(office.NeedsReview);
        Assert.Null(office.ReviewReason);
    }

    [Fact]
    public void ReassignOwnerOnReconnect_OnlyMovesADisconnectedPersonalRowOfSomeoneElse()
    {
        var formerOwner = Guid.NewGuid();
        var newOwner = Guid.NewGuid();
        var personal = TenantEmailAccount
            .Create(TenantId, "jane@gmail.com", ProviderCode.Gmail, formerOwner, Now, ownerUserId: formerOwner)
            .Value;
        personal.MarkConnected(Now);

        Assert.False(personal.ReassignOwnerOnReconnect(newOwner)); // conectada: no se toca
        personal.Disconnect(Now);
        Assert.False(personal.ReassignOwnerOnReconnect(formerOwner)); // mismo dueño
        Assert.True(personal.ReassignOwnerOnReconnect(newOwner));
        Assert.Equal(newOwner, personal.OwnerUserId);
        Assert.Equal(formerOwner, personal.CreatedByUserId);

        var office = CreateValidAccount();
        office.MarkConnected(Now);
        office.Disconnect(Now);
        Assert.False(office.ReassignOwnerOnReconnect(newOwner)); // la de oficina sigue de oficina
        Assert.True(office.IsOffice);
    }
}
