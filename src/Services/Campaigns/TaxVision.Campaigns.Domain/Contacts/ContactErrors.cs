using BuildingBlocks.Results;

namespace TaxVision.Campaigns.Domain.Contacts;

/// <summary>Errores de dominio centralizados del aggregate <see cref="Contact"/>.</summary>
public static class ContactErrors
{
    public static readonly Error TenantRequired = new("Contact.Tenant", "TenantId is required.");
    public static readonly Error DestinationRequired = new(
        "Contact.Destination",
        "A contact needs at least an email or a phone number."
    );
    public static readonly Error NameTooLong = new("Contact.NameTooLong", "Name exceeds the maximum length.");
    public static readonly Error EmailTooLong = new("Contact.EmailTooLong", "Email exceeds the maximum length.");
    public static readonly Error PhoneTooLong = new("Contact.PhoneTooLong", "Phone exceeds the maximum length.");
    public static readonly Error PhoneNotE164 = new(
        "Contact.PhoneNotE164",
        "Phone must be in international E.164 format with a country code, e.g. +18095550142."
    );
    public static readonly Error NotFound = new("Contact.NotFound", "Contact not found.");
}
