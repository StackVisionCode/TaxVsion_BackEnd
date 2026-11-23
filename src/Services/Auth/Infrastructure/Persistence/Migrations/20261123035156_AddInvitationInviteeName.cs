using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvitationInviteeName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InviteeFirstName",
                table: "Invitations",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "InviteeLastName",
                table: "Invitations",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "InviteeFirstName", table: "Invitations");

            migrationBuilder.DropColumn(name: "InviteeLastName", table: "Invitations");
        }
    }
}
