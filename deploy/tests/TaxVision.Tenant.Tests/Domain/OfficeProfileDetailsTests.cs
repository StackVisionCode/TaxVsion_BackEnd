using TaxVision.Tenant.Domain.Profiles;
using TaxVision.Tenant.Tests.TestSupport;

namespace TaxVision.Tenant.Tests.Domain;

/// <summary>Cada campo inválido devuelve el código del contrato (<c>Tenant.Profile.*</c>).</summary>
public sealed class OfficeProfileDetailsTests
{
    public static TheoryData<OfficeProfileDraft, string> InvalidDrafts()
    {
        var full = OfficeProfileDrafts.Full();
        var address = full.PhysicalAddress!;
        return new()
        {
            { full with { LegalName = new string('a', 201) }, "Tenant.Profile.LegalName" },
            { full with { DisplayName = new string('a', 201) }, "Tenant.Profile.DisplayName" },
            { full with { DisplayName = "Acme IRS Help" }, "Tenant.Profile.DisplayName" },
            { full with { DisplayName = "internal revenue  service desk" }, "Tenant.Profile.DisplayName" },
            { full with { EntityType = "Corporation" }, "Tenant.Profile.EntityType" },
            { full with { EntityType = "2" }, "Tenant.Profile.EntityType" },
            { full with { Ein = "12-34567" }, "Tenant.Profile.Ein" },
            { full with { Email = "office@acme" }, "Tenant.Profile.Email" },
            { full with { Website = "http://localhost" }, "Tenant.Profile.Website" },
            { full with { Website = "ftp://acmetax.com" }, "Tenant.Profile.Website" },
            { full with { Phone = "12345" }, "Tenant.Profile.Phone" },
            { full with { PhysicalAddress = address with { State = "ZZ" } }, "Tenant.Profile.State" },
            { full with { PhysicalAddress = address with { PostalCode = "7870" } }, "Tenant.Profile.PostalCode" },
            { full with { MailingAddress = address with { State = "Texas" } }, "Tenant.Profile.State" },
            { full with { PhysicalAddress = address with { Line1 = new string('a', 201) } }, "Tenant.Profile.Address" },
            { full with { DefaultLanguage = "fr" }, "Tenant.Profile.Language" },
            {
                full with
                {
                    BusinessHours = [new("Monday", "09:00", "17:00"), new("MONDAY", "10:00", "12:00")],
                },
                "Tenant.Profile.BusinessHours"
            },
            { full with { BusinessHours = [new("Monday", "9am", "17:00")] }, "Tenant.Profile.BusinessHours" },
            { full with { BusinessHours = [new("Monday", "17:00", "17:00")] }, "Tenant.Profile.BusinessHours" },
            { full with { BusinessHours = [new("Funday", "09:00", "17:00")] }, "Tenant.Profile.BusinessHours" },
            { full with { BookingUrl = "not a url" }, "Tenant.Profile.BookingUrl" },
            { full with { ReviewUrl = "javascript:alert(1)" }, "Tenant.Profile.ReviewUrl" },
        };
    }

    [Theory]
    [MemberData(nameof(InvalidDrafts))]
    public void An_invalid_field_returns_its_contract_code(OfficeProfileDraft draft, string expectedCode)
    {
        var result = OfficeProfileDetails.Create(draft);

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Code);
    }

    [Fact]
    public void Outside_the_US_state_and_postal_code_are_free_text()
    {
        var draft = OfficeProfileDrafts.Empty() with
        {
            PhysicalAddress = new OfficeAddressDraft(
                "10 Downing St",
                null,
                "London",
                "Greater London",
                "SW1A 2AA",
                "gb"
            ),
        };

        var result = OfficeProfileDetails.Create(draft);

        Assert.True(result.IsSuccess);
        Assert.Equal("GB", result.Value.PhysicalAddress!.Country);
        Assert.Equal("SW1A 2AA", result.Value.PhysicalAddress.PostalCode);
    }

    [Fact]
    public void A_blank_mailing_address_counts_as_same_as_physical()
    {
        var draft = OfficeProfileDrafts.Full() with
        {
            MailingAddress = new OfficeAddressDraft(" ", null, "", null, null, "US"),
        };

        var result = OfficeProfileDetails.Create(draft);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.MailingAddress);
    }

    [Fact]
    public void Missing_language_defaults_to_english()
    {
        var result = OfficeProfileDetails.Create(OfficeProfileDrafts.Empty());

        Assert.Equal("en", result.Value.DefaultLanguage);
        Assert.Empty(result.Value.BusinessHours.Days);
    }
}
