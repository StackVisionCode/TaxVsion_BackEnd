using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMileageModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Bases existentes: el seeder es pasivo. Sin precio: lo pone MileageModuleRevision por el dominio.
            migrationBuilder.Sql(
                """
                IF NOT EXISTS (SELECT 1 FROM [Modules] WHERE [Code] = 'mileage')
                    INSERT INTO [Modules] ([Id], [Code], [Name], [Description], [Category], [AddonPriceMonthlyUsd], [AddonPriceYearlyUsd])
                    VALUES ('b2000000-0000-0000-0000-00000000000b', 'mileage', 'Mileage',
                            'GPS trip tracking, vehicle management and mileage reporting.', 'Commercial', NULL, NULL);
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM [Modules] WHERE [Id] = 'b2000000-0000-0000-0000-00000000000b';");
        }
    }
}
