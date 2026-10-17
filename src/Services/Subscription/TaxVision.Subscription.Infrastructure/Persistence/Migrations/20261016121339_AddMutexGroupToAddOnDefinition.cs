using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMutexGroupToAddOnDefinition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MutexGroup",
                table: "AddOnDefinitions",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "MutexGroup", table: "AddOnDefinitions");
        }
    }
}
