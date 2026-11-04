using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Campaigns.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakeHandoverSourceEventOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "SourceEventId",
                table: "CampaignScheduleHandovers",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Las reanudaciones no tienen evento: sin esto el NOT NULL falla con filas existentes.
            migrationBuilder.Sql(
                "UPDATE [CampaignScheduleHandovers] SET [SourceEventId] = '00000000-0000-0000-0000-000000000000' WHERE [SourceEventId] IS NULL;"
            );

            migrationBuilder.AlterColumn<Guid>(
                name: "SourceEventId",
                table: "CampaignScheduleHandovers",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true
            );
        }
    }
}
