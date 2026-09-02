using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Correspondence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIncomingEmailSenderAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthDkim",
                table: "IncomingEmails",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<string>(
                name: "AuthDmarc",
                table: "IncomingEmails",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: ""
            );

            migrationBuilder.AddColumn<string>(
                name: "AuthSpf",
                table: "IncomingEmails",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: ""
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AuthDkim", table: "IncomingEmails");

            migrationBuilder.DropColumn(name: "AuthDmarc", table: "IncomingEmails");

            migrationBuilder.DropColumn(name: "AuthSpf", table: "IncomingEmails");
        }
    }
}
