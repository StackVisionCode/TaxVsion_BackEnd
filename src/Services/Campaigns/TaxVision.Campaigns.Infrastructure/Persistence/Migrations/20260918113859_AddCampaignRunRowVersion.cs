using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Campaigns.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignRunRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "CampaignRuns",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "RowVersion", table: "CampaignRuns");
        }
    }
}
