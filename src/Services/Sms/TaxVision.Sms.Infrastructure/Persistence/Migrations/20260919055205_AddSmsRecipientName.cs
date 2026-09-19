using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Sms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSmsRecipientName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RecipientName",
                table: "smsMessages",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "RecipientName", table: "smsMessages");
        }
    }
}
