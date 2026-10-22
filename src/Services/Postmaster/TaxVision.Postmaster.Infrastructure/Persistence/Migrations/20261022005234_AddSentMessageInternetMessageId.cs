using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Postmaster.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSentMessageInternetMessageId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InternetMessageId",
                table: "SentMessages",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_SentMessages_InternetMessageId",
                table: "SentMessages",
                column: "InternetMessageId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_SentMessages_InternetMessageId", table: "SentMessages");

            migrationBuilder.DropColumn(name: "InternetMessageId", table: "SentMessages");
        }
    }
}
