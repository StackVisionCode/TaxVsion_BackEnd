using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Growth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLateCommitReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LateCommitReviews",
                schema: "codes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodeReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentSource = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RelatedPaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    Resolution = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LateCommitReviews", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LateCommitReviews_Pending",
                schema: "codes",
                table: "LateCommitReviews",
                columns: new[] { "ResolvedAtUtc", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_LateCommitReviews_Reservation_Event",
                schema: "codes",
                table: "LateCommitReviews",
                columns: new[] { "CodeReservationId", "SourceEventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LateCommitReviews",
                schema: "codes");
        }
    }
}
