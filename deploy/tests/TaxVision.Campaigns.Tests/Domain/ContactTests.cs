using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Contacts;

namespace TaxVision.Campaigns.Tests.Domain;

public sealed class ContactTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void Create_requires_at_least_one_destination()
    {
        var result = Contact.Create(Tenant, "No Dest", null, null, ContactSource.Manual);
        Assert.True(result.IsFailure);
        Assert.Equal(ContactErrors.DestinationRequired, result.Error);
    }

    [Fact]
    public void Create_normalizes_email_to_lowercase_and_trims()
    {
        var contact = Contact.Create(Tenant, "  Ana  ", "  ANA@Example.COM ", null, ContactSource.Import).Value;
        Assert.Equal("ana@example.com", contact.Email);
        Assert.Equal("Ana", contact.Name);
        Assert.Null(contact.PhoneE164);
    }

    [Fact]
    public void Create_with_only_phone_is_allowed()
    {
        var contact = Contact.Create(Tenant, null, null, "+18295550000", ContactSource.Manual).Value;
        Assert.Equal("+18295550000", contact.PhoneE164);
        Assert.Null(contact.Email);
    }

    [Fact]
    public void OptOut_and_OptIn_toggle_per_channel()
    {
        var contact = Contact.Create(Tenant, null, "a@x.com", "+18295550000", ContactSource.Manual).Value;

        contact.OptOut(CampaignChannel.Email);
        Assert.True(contact.IsOptedOut(CampaignChannel.Email));
        Assert.False(contact.IsOptedOut(CampaignChannel.Sms));

        contact.OptIn(CampaignChannel.Email);
        Assert.False(contact.IsOptedOut(CampaignChannel.Email));
    }

    [Fact]
    public void DestinationFor_returns_channel_appropriate_value()
    {
        var contact = Contact.Create(Tenant, null, "a@x.com", "+18295550000", ContactSource.Manual).Value;
        Assert.Equal("a@x.com", contact.DestinationFor(CampaignChannel.Email));
        Assert.Equal("+18295550000", contact.DestinationFor(CampaignChannel.Sms));
        Assert.Equal("+18295550000", contact.DestinationFor(CampaignChannel.WhatsApp));
        Assert.Null(contact.DestinationFor(CampaignChannel.Push));
    }
}

public sealed class ContactListTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void AddMember_is_idempotent()
    {
        var list = ContactList.Create(Tenant, "L", null).Value;
        var contactId = Guid.NewGuid();

        list.AddMember(contactId);
        list.AddMember(contactId);

        Assert.Single(list.Members);
    }

    [Fact]
    public void RemoveMember_removes_and_reports_missing()
    {
        var list = ContactList.Create(Tenant, "L", null).Value;
        var contactId = Guid.NewGuid();
        list.AddMember(contactId);

        Assert.True(list.RemoveMember(contactId).IsSuccess);
        Assert.Empty(list.Members);
        Assert.True(list.RemoveMember(contactId).IsFailure);
    }

    [Fact]
    public void Create_requires_name()
    {
        Assert.True(ContactList.Create(Tenant, "  ", null).IsFailure);
    }
}
