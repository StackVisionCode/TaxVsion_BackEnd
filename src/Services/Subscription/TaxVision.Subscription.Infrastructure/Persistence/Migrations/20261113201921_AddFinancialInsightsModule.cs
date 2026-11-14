using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialInsightsModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM [Modules] WHERE [Code] = 'financial_insights')
                    INSERT INTO [Modules] ([Id], [Code], [Name], [Description], [Category], [AddonPriceMonthlyUsd], [AddonPriceYearlyUsd])
                    VALUES ('b2000000-0000-0000-0000-00000000000a', 'financial_insights', 'Financial Insights',
                            'Bank statement imports and financial analysis.', 'Commercial', NULL, NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM [Modules] WHERE [Id] = 'b2000000-0000-0000-0000-00000000000a';");
        }
    }
}
