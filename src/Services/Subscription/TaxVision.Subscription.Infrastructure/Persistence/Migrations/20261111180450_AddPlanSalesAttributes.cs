using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanSalesAttributes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFeatured",
                table: "SubscriptionPlans",
                type: "bit",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<bool>(
                name: "IsOnSale",
                table: "SubscriptionPlans",
                type: "bit",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "SubscriptionPlans",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]
            );

            migrationBuilder.AddColumn<int>(
                name: "SalesRank",
                table: "SubscriptionPlans",
                type: "int",
                nullable: false,
                defaultValue: 100
            );

            // Deja el catálogo como se ve hoy: lo publicado sigue a la venta, en el orden de siempre y con Pro
            // destacado (la landing lo resaltaba por código). sp_executesql porque las columnas son de este mismo lote.
            migrationBuilder.Sql(
                """
                EXEC sp_executesql N'
                UPDATE SubscriptionPlans SET IsOnSale = 1 WHERE Status = ''Published'';
                UPDATE SubscriptionPlans SET SalesRank = CASE Code
                    WHEN ''starter'' THEN 1
                    WHEN ''pro'' THEN 2
                    WHEN ''enterprise'' THEN 3
                    ELSE 100 END;
                UPDATE SubscriptionPlans SET IsFeatured = 1 WHERE Code = ''pro'';';
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "IsFeatured", table: "SubscriptionPlans");

            migrationBuilder.DropColumn(name: "IsOnSale", table: "SubscriptionPlans");

            migrationBuilder.DropColumn(name: "RowVersion", table: "SubscriptionPlans");

            migrationBuilder.DropColumn(name: "SalesRank", table: "SubscriptionPlans");
        }
    }
}
