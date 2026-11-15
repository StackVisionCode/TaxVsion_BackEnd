using BuildingBlocks.TimeZones;
using TaxVision.PaymentClient.Domain.Recurring;
using TaxVision.PaymentClient.Domain.ValueObjects;
using Xunit;

namespace TaxVision.PaymentClient.Tests.Domain;

/// <summary>
/// P10 (TZ05-H14): un recurrente mensual anclado se calcula desde su ancla civil, no desde la ocurrencia anterior. El
/// del 31 vuelve al 31 después de febrero y mantiene la hora local a través del cambio de horario.
/// </summary>
public sealed class TenantRecurringPaymentCivilAnchorTests
{
    private static readonly ResolvedTimeZone NewYork = ResolvedTimeZone.Resolve("America/New_York");

    [Fact]
    public void A_monthly_plan_on_the_31st_keeps_the_31st_and_the_local_hour()
    {
        // 9:00 del 31-ene en Nueva York (EST) = 14:00Z.
        var start = new DateTime(2027, 1, 31, 14, 0, 0, DateTimeKind.Utc);
        var plan = Plan(start);
        plan.AnchorToOfficeZone(NewYork);

        plan.GenerateSchedules(3, start);

        Assert.Equal(
            [
                new DateTime(2027, 1, 31, 14, 0, 0, DateTimeKind.Utc),
                new DateTime(2027, 2, 28, 14, 0, 0, DateTimeKind.Utc),
                // 31-mar a las 9:00 EDT = 13:00Z (encadenando AddMonths quedaba el 28-mar a las 14:00Z).
                new DateTime(2027, 3, 31, 13, 0, 0, DateTimeKind.Utc),
            ],
            plan.Schedules.Select(s => s.ScheduledDate).ToList()
        );
        Assert.Equal("America/New_York", plan.TimeZoneId);
    }

    [Fact]
    public void Without_a_known_office_zone_the_plan_is_not_anchored()
    {
        var plan = Plan(new DateTime(2027, 1, 31, 14, 0, 0, DateTimeKind.Utc));

        plan.AnchorToOfficeZone(ResolvedTimeZone.Resolve(null));

        Assert.Null(plan.TimeZoneId);
    }

    private static TenantRecurringPayment Plan(DateTime start) =>
        TenantRecurringPayment
            .Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                "pm_123",
                Money.Create(1999, "USD").Value,
                PaymentPurpose.Create(PaymentPurposeKind.RetainerPayment, "plan-001").Value,
                BillingCycle.Monthly,
                null,
                start,
                null,
                null,
                RetryPolicy.Default,
                null,
                null,
                Guid.Empty,
                start
            )
            .Value;
}
