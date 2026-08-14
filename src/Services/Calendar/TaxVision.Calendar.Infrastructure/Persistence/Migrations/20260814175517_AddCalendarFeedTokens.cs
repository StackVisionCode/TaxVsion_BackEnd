using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Calendar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarFeedTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CalendarFeedTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "varbinary(32)", nullable: false),
                    TokenLast4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastAccessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarFeedTokens", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CalendarFeedTokens_TenantId_UserId",
                table: "CalendarFeedTokens",
                columns: new[] { "TenantId", "UserId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CalendarFeedTokens_TokenHash",
                table: "CalendarFeedTokens",
                column: "TokenHash",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CalendarFeedTokens");
        }
    }
}
