using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChargeConceptToSaaSPayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConceptCode",
                table: "SaaSPayments",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "ConceptDisplayName",
                table: "SaaSPayments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ConceptCode", table: "SaaSPayments");

            migrationBuilder.DropColumn(name: "ConceptDisplayName", table: "SaaSPayments");
        }
    }
}
