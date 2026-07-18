using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Postmaster.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailIdempotency",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SentMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailIdempotency", x => new { x.TenantId, x.IdempotencyKey });
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_EmailIdempotency_ExpiresAtUtc",
                table: "EmailIdempotency",
                column: "ExpiresAtUtc"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "EmailIdempotency");
        }
    }
}
