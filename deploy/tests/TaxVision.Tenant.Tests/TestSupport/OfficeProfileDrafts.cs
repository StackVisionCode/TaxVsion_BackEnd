using TaxVision.Tenant.Domain.Profiles;

namespace TaxVision.Tenant.Tests.TestSupport;

internal static class OfficeProfileDrafts
{
    public static OfficeProfileDraft Empty() =>
        new(null, null, null, null, null, null, null, null, true, null, null, null, null, null);

    public static OfficeProfileDraft Full() =>
        new(
            LegalName: "Acme Tax Services LLC",
            DisplayName: "Acme Tax",
            EntityType: "llc",
            Ein: "123456789",
            Phone: "555.123.4567",
            Email: "office@acmetax.com",
            Website: "acmetax.com",
            PhysicalAddress: new OfficeAddressDraft("100 Main St", "Suite 2", "Austin", "tx", "787011234", "USA"),
            MailingSameAsPhysical: false,
            MailingAddress: new OfficeAddressDraft("PO Box 9", null, "Austin", "TX", "78702", null),
            DefaultLanguage: "ES",
            BusinessHours:
            [
                new BusinessHoursDraft("Saturday", "10:00", "14:00"),
                new BusinessHoursDraft("monday", "09:00", "18:00"),
            ],
            BookingUrl: "https://calendly.com/acmetax",
            ReviewUrl: "g.page/r/acmetax/review"
        );
}
