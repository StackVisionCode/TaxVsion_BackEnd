using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Calendar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOfficeScheduleProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OfficeSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TimeZoneUpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProfileVersion = table.Column<long>(type: "bigint", nullable: false),
                    HoursJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ClosuresJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficeSchedules", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_OfficeSchedules_TenantId",
                table: "OfficeSchedules",
                column: "TenantId",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "OfficeSchedules");
        }
    }
}
