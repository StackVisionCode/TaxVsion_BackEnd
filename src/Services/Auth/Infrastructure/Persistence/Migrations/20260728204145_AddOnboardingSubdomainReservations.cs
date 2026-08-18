using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingSubdomainReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OnboardingSubdomainReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(63)", maxLength: 63, nullable: false),
                    OnboardingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReservedByEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnboardingSubdomainReservations", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_OnboardingSubdomainReservations_OnboardingId",
                table: "OnboardingSubdomainReservations",
                column: "OnboardingId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_OnboardingSubdomainReservations_Slug_ConsumedAtUtc_ExpiresAtUtc",
                table: "OnboardingSubdomainReservations",
                columns: new[] { "Slug", "ConsumedAtUtc", "ExpiresAtUtc" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "OnboardingSubdomainReservations");
        }
    }
}
