using TaxVision.FinancialInsights.Domain.Imports;
using TaxVision.FinancialInsights.Domain.Projections;

namespace TaxVision.FinancialInsights.Tests.Domain;

public sealed class FinancialMovementTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();
    private static readonly Guid Document = Guid.NewGuid();
    private static readonly DateOnly Date = new(2026, 1, 15);

    [Theory]
    [InlineData(0, "USD", "debit", "Coffee", "Meals", 1, "Import.Movement.InvalidAmount")]
    [InlineData(1_000_000_001, "USD", "debit", "Coffee", "Meals", 1, "Import.Movement.InvalidAmount")]
    [InlineData(10, "US", "debit", "Coffee", "Meals", 1, "Import.Movement.InvalidCurrency")]
    [InlineData(10, "U5D", "debit", "Coffee", "Meals", 1, "Import.Movement.InvalidCurrency")]
    [InlineData(10, "USD", "refund", "Coffee", "Meals", 1, "Import.Movement.InvalidDirection")]
    [InlineData(10, "USD", "debit", " ", "Meals", 1, "Import.Movement.DescriptionRequired")]
    [InlineData(10, "USD", "debit", "Coffee", "", 1, "Import.Movement.CategoryRequired")]
    [InlineData(10, "USD", "debit", "Coffee", "Meals", 0, "Import.Movement.InvalidSourcePage")]
    public void Invalid_suggestions_are_failures_not_exceptions(
        double amount,
        string currency,
        string direction,
        string description,
        string category,
        int page,
        string code
    )
    {
        var result = FinancialMovement.Create(
            Tenant,
            Customer,
            Document,
            Date,
            description,
            (decimal)amount,
            currency,
            direction,
            category,
            page
        );

        Assert.Equal(code, result.Error.Code);
    }

    [Fact]
    public void Valid_suggestion_is_normalized_and_requires_review()
    {
        var movement = FinancialMovement
            .Create(Tenant, Customer, Document, Date, "  Coffee shop ", 12.5m, "usd", "debit", " Meals ", 2)
            .Value;

        Assert.Equal("USD", movement.Currency);
        Assert.Equal("Coffee shop", movement.Description);
        Assert.Equal("Meals", movement.SuggestedCategory);
        Assert.True(movement.RequiresReview);
        Assert.Equal(Tenant, movement.TenantId);
    }

    [Fact]
    public void Reservation_counts_one_document_per_month_and_records_usage_once()
    {
        var now = new DateTime(2026, 3, 18, 10, 0, 0, DateTimeKind.Utc);

        Assert.Equal("Import.Reservation.ModelRequired", AiProcessingReservation.Create(Tenant, Document, " ", now).Error.Code);
        Assert.Equal("Import.DocumentRequired", AiProcessingReservation.Create(Tenant, Guid.Empty, "m", now).Error.Code);

        var reservation = AiProcessingReservation.Create(Tenant, Document, "gpt-5.4-mini", now).Value;
        Assert.Equal(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), reservation.MonthStartUtc);
        Assert.True(reservation.RecordUsage(120, -5).IsSuccess);
        Assert.Equal(0, reservation.OutputTokens);
        Assert.Equal("Import.Reservation.AlreadyCompleted", reservation.RecordUsage(1, 1).Error.Code);
    }

    [Fact]
    public void Monthly_document_limit_comes_from_the_plan_and_falls_back_to_configuration()
    {
        var withPlan = TenantEntitlementsProjection.Create(Tenant, "enterprise", 1, ["financial_insights"], 200);
        var withoutKey = TenantEntitlementsProjection.Create(Tenant, "enterprise", 1, ["financial_insights"], null);

        Assert.Equal(200, withPlan.MonthlyDocumentLimit(10));
        Assert.Equal(10, withoutKey.MonthlyDocumentLimit(10));
        Assert.True(withPlan.HasModule("FINANCIAL_INSIGHTS"));
        Assert.False(withPlan.HasModule("documents"));
    }
}
