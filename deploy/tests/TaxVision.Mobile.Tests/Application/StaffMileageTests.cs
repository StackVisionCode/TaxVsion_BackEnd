using TaxVision.Mobile.Application.Staff;
using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Tests.Application;

public sealed class StaffMileageTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly Guid Car = Guid.NewGuid();
    private static readonly Guid Truck = Guid.NewGuid();
    private const double Mile = 1609.344;

    private static StaffTripRow Row(
        string purpose,
        double miles,
        DateTime startedUtc,
        TripStatus status = TripStatus.Completed,
        Guid? vehicle = null,
        decimal? tripRate = null,
        string? notes = null
    ) =>
        new(
            Guid.NewGuid(),
            vehicle ?? Car,
            vehicle == Truck ? "TRUCK-1" : "CAR-1",
            status,
            purpose,
            notes,
            startedUtc,
            status == TripStatus.Completed ? startedUtc.AddMinutes(20) : null,
            miles * Mile,
            1200,
            15,
            tripRate,
            tripRate is null ? null : "USD",
            null,
            12
        );

    [Fact]
    public void The_summary_counts_only_completed_trips_and_groups_by_purpose_vehicle_and_month()
    {
        var rows = new[]
        {
            Row("business", 10, new DateTime(2026, 3, 2, 15, 0, 0, DateTimeKind.Utc)),
            Row("business", 5, new DateTime(2026, 8, 2, 15, 0, 0, DateTimeKind.Utc), vehicle: Truck),
            Row("medical", 4, new DateTime(2026, 3, 9, 15, 0, 0, DateTimeKind.Utc)),
            Row("personal", 3, new DateTime(2026, 3, 9, 18, 0, 0, DateTimeKind.Utc)),
            Row("business", 50, new DateTime(2026, 3, 10, 15, 0, 0, DateTimeKind.Utc), TripStatus.Cancelled),
            Row("business", 7, new DateTime(2026, 3, 11, 15, 0, 0, DateTimeKind.Utc), TripStatus.InProgress),
        };

        var summary = StaffMileageSummary.Build(2026, rows, [], IrsStandardMileageRates.All, Utc);

        Assert.Equal(4, summary.CompletedTrips);
        Assert.Equal(22, summary.TotalMiles, 3);
        Assert.Equal(1, summary.InProgressTrips);
        var business = Assert.Single(summary.ByPurpose, p => p.Purpose == "business");
        Assert.Equal(2, business.Trips);
        Assert.Equal(15, business.Miles, 3);
        // 10 mi a 0.725 (marzo) + 5 mi a 0.76 (agosto).
        Assert.Equal(7.25m + 3.80m, business.EstimatedAmount);
        Assert.Equal(0.82m, Assert.Single(summary.ByPurpose, p => p.Purpose == "medical").EstimatedAmount);
        Assert.Equal(0m, Assert.Single(summary.ByPurpose, p => p.Purpose == "personal").EstimatedAmount);
        Assert.Equal(7.25m + 3.80m + 0.82m, summary.EstimatedAmount);
        Assert.Equal(17, Assert.Single(summary.ByVehicle, v => v.VehicleId == Car).Miles, 3);
        Assert.Equal(5, Assert.Single(summary.ByVehicle, v => v.VehicleId == Truck).Miles, 3);
        var march = Assert.Single(summary.ByMonth, m => m.Month == 3);
        Assert.Equal(10, march.MilesByPurpose["business"], 3);
        Assert.Equal(4, march.MilesByPurpose["medical"], 3);
        Assert.Equal(12, summary.ByMonth.Count);
    }

    [Fact]
    public void The_summary_uses_the_trip_rate_before_the_irs_rate()
    {
        var rows = new[] { Row("business", 10, new DateTime(2026, 3, 2, 15, 0, 0, DateTimeKind.Utc), tripRate: 0.5m) };

        var summary = StaffMileageSummary.Build(2026, rows, [], IrsStandardMileageRates.All, Utc);

        Assert.Equal(5.00m, summary.EstimatedAmount);
    }

    [Fact]
    public void The_default_tax_year_is_the_offices_current_year()
    {
        var newYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        // 2 a. m. UTC del 1 de enero todavía es 31 de diciembre en Nueva York.
        var utc = new DateTime(2027, 1, 1, 2, 0, 0, DateTimeKind.Utc);

        Assert.Equal(2026, StaffYear.Current(newYork, utc));
        Assert.Equal(2027, StaffYear.Current(TimeZoneInfo.Utc, utc));
    }

    [Fact]
    public void Amounts_in_different_currencies_are_not_added_together()
    {
        var office = new[] { new RatePeriod("business", new DateOnly(2026, 1, 1), 0.50m, "CAD") };
        var rows = new[]
        {
            Row("business", 10, new DateTime(2026, 3, 2, 15, 0, 0, DateTimeKind.Utc)),
            Row("medical", 4, new DateTime(2026, 3, 9, 15, 0, 0, DateTimeKind.Utc)),
        };

        var summary = StaffMileageSummary.Build(2026, rows, office, IrsStandardMileageRates.All, Utc);

        Assert.Null(summary.EstimatedAmount);
        Assert.Equal(5.00m, Assert.Single(summary.EstimatedAmounts, a => a.Currency == "CAD").Amount);
        Assert.Equal(0.82m, Assert.Single(summary.EstimatedAmounts, a => a.Currency == "USD").Amount);
    }

    [Fact]
    public void With_one_currency_the_total_is_given_with_its_currency()
    {
        var rows = new[] { Row("business", 10, new DateTime(2026, 3, 2, 15, 0, 0, DateTimeKind.Utc)) };

        var summary = StaffMileageSummary.Build(2026, rows, [], IrsStandardMileageRates.All, Utc);

        Assert.Equal(7.25m, summary.EstimatedAmount);
        Assert.Equal("USD", Assert.Single(summary.EstimatedAmounts).Currency);
    }

    [Fact]
    public void A_trip_response_says_where_its_rate_came_from_and_whether_the_route_was_removed()
    {
        var purged = Row("business", 10, new DateTime(2026, 3, 2, 15, 0, 0, DateTimeKind.Utc)) with
        {
            CoordinatesPurgedAtUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        var response = StaffTripResponse.From(purged, [], IrsStandardMileageRates.All, Utc);

        Assert.Equal("Standard", response.RateSource);
        Assert.Equal(0.725m, response.RatePerMile);
        Assert.Equal(7.25m, response.EstimatedAmount);
        Assert.True(response.RouteRemoved);
        Assert.Null(
            StaffTripResponse
                .From(Row("personal", 3, purged.StartedAtUtc), [], IrsStandardMileageRates.All, Utc)
                .RateSource
        );
    }

    [Fact]
    public void The_csv_neutralizes_values_that_a_spreadsheet_would_run_as_formulas()
    {
        var rows = new[]
        {
            Row("business", 10, new DateTime(2026, 3, 2, 15, 0, 0, DateTimeKind.Utc), notes: "=HYPERLINK(\"x\")"),
            Row("business", 2, new DateTime(2026, 3, 3, 15, 0, 0, DateTimeKind.Utc), notes: "Client, \"meeting\""),
            Row("business", 50, new DateTime(2026, 3, 4, 15, 0, 0, DateTimeKind.Utc), TripStatus.Cancelled),
        };

        var csv = StaffTripsCsv.Write(rows, [], IrsStandardMileageRates.All, Utc);
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("Date,Purpose,Notes,Vehicle,Miles,Duration (min),Rate per mile,Estimated amount", lines[0]);
        Assert.Equal(3, lines.Length);
        Assert.Contains("\"'=HYPERLINK(\"\"x\"\")\"", lines[1]);
        Assert.Contains("\"Client, \"\"meeting\"\"\"", lines[2]);
    }

    [Fact]
    public async Task A_staff_member_sees_a_customer_if_assigned_or_with_view_all()
    {
        var tenant = Guid.NewGuid();
        var staff = Guid.NewGuid();
        var assigned = Guid.NewGuid();
        var store = new Assignments(tenant, staff, assigned);

        Assert.True(await StaffCustomerAccess.CanSeeAsync(new(tenant, staff, false, assigned), store, default));
        Assert.False(await StaffCustomerAccess.CanSeeAsync(new(tenant, staff, false, Guid.NewGuid()), store, default));
        Assert.True(await StaffCustomerAccess.CanSeeAsync(new(tenant, staff, true, Guid.NewGuid()), store, default));
        Assert.False(
            await StaffCustomerAccess.CanSeeAsync(new(Guid.NewGuid(), staff, false, assigned), store, default)
        );
    }

    private sealed class Assignments(Guid tenant, Guid staff, Guid customer) : IStaffCustomerAssignments
    {
        public Task<bool> IsAssignedAsync(Guid tenantId, Guid userId, Guid customerId, CancellationToken ct) =>
            Task.FromResult(tenantId == tenant && userId == staff && customerId == customer);
    }
}
