using BuildingBlocks.Results;

namespace TaxVision.Campaigns.Domain.Contacts;

/// <summary>Errores de dominio centralizados del aggregate <see cref="ContactList"/>.</summary>
public static class ContactListErrors
{
    public static readonly Error TenantRequired = new("ContactList.Tenant", "TenantId is required.");
    public static readonly Error NameRequired = new("ContactList.Name", "Name is required.");
    public static readonly Error NameTooLong = new("ContactList.NameTooLong", "Name exceeds the maximum length.");
    public static readonly Error DescriptionTooLong = new(
        "ContactList.DescriptionTooLong",
        "Description exceeds the maximum length."
    );
    public static readonly Error MemberNotFound = new(
        "ContactList.MemberNotFound",
        "Contact is not a member of this list."
    );
    public static readonly Error NotFound = new("ContactList.NotFound", "Contact list not found.");
}
