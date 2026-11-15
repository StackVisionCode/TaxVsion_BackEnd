using BuildingBlocks.TimeZones;
using TaxVision.Tasks.ClientForms.Application.Actions;
using Xunit;

namespace TaxVision.Tasks.Tests.ClientForms;

/// <summary>TZ08-H3: sin año fiscal, la carpeta de firma usa el año de la oficina, no el UTC.</summary>
public sealed class SubmissionSummaryRequesterTests
{
    [Fact]
    public void Without_a_tax_year_the_office_year_is_used()
    {
        // 20:00 del 31-dic en Nueva York = 01:00Z del 1-ene.
        var year = SubmissionSummaryRequester.DefaultTaxYear(
            null,
            new DateTime(2027, 1, 1, 1, 0, 0, DateTimeKind.Utc),
            ResolvedTimeZone.Resolve("America/New_York")
        );

        Assert.Equal(2026, year);
    }

    [Fact]
    public void A_given_tax_year_wins()
    {
        Assert.Equal(2025, SubmissionSummaryRequester.DefaultTaxYear(2025, DateTime.UtcNow, ResolvedTimeZone.Utc));
    }
}
