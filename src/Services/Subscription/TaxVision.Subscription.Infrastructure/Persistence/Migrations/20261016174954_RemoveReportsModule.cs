using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveReportsModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [Modules] WHERE [Code] = N'reports')
                    DELETE FROM [Modules] WHERE [Code] = N'reports';

                IF EXISTS (SELECT 1 FROM [AddOnDefinitions] WHERE [Code] = N'module-addon-reports')
                    UPDATE [AddOnDefinitions]
                    SET [Status] = N'Deprecated'
                    WHERE [Code] = N'module-addon-reports';
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF NOT EXISTS (SELECT 1 FROM [Modules] WHERE [Code] = N'reports')
                    INSERT INTO [Modules]
                        ([Id], [Code], [Name], [Description], [Category], [AddonPriceMonthlyUsd], [AddonPriceYearlyUsd])
                    VALUES
                        ('b2000000-0000-0000-0000-00000000000a', N'reports', N'Reports',
                         N'Operational reports reserved for a future release.', N'Commercial', NULL, NULL);
                """
            );
        }
    }
}
