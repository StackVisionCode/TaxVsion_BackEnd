using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Calendar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookingPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    BookableTypeIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MinNoticeMinutes = table.Column<int>(type: "int", nullable: false),
                    MaxDaysAhead = table.Column<int>(type: "int", nullable: false),
                    BufferMinutes = table.Column<int>(type: "int", nullable: false),
                    SlotStepMinutes = table.Column<int>(type: "int", nullable: false),
                    CustomerChangeCutoffMinutes = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingPolicies", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_BookingPolicies_TenantId",
                table: "BookingPolicies",
                column: "TenantId",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "BookingPolicies");
        }
    }
}
