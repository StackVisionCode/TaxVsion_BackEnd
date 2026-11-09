using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormAccessGrantRowVersionAndIntakeDailyCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "FormAccessGrants",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]
            );

            migrationBuilder.CreateTable(
                name: "FormIntakeDailyCounters",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DayUtc = table.Column<DateTime>(type: "date", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormIntakeDailyCounters", x => new { x.TenantId, x.DayUtc });
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FormIntakeDailyCounters");

            migrationBuilder.DropColumn(name: "RowVersion", table: "FormAccessGrants");
        }
    }
}
