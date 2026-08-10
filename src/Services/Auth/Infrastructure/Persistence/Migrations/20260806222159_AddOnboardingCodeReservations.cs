using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingCodeReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "TenantOnboardings",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true
            );

            migrationBuilder.AddColumn<bool>(
                name: "FullyCovered",
                table: "TenantOnboardings",
                type: "bit",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<long>(
                name: "GrossAmountCents",
                table: "TenantOnboardings",
                type: "bigint",
                nullable: true
            );

            migrationBuilder.AddColumn<long>(
                name: "NetAmountCents",
                table: "TenantOnboardings",
                type: "bigint",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "ReferralAttributionId",
                table: "TenantOnboardings",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<long>(
                name: "TotalDiscountCents",
                table: "TenantOnboardings",
                type: "bigint",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "OnboardingCodeReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OnboardingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodeReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BenefitType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DiscountCents = table.Column<long>(type: "bigint", nullable: false),
                    SnapshotHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnboardingCodeReservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OnboardingCodeReservations_TenantOnboardings_OnboardingId",
                        column: x => x.OnboardingId,
                        principalTable: "TenantOnboardings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_OnboardingCodeReservations_CodeReservationId",
                table: "OnboardingCodeReservations",
                column: "CodeReservationId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_OnboardingCodeReservations_OnboardingId",
                table: "OnboardingCodeReservations",
                column: "OnboardingId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "OnboardingCodeReservations");

            migrationBuilder.DropColumn(name: "Currency", table: "TenantOnboardings");

            migrationBuilder.DropColumn(name: "FullyCovered", table: "TenantOnboardings");

            migrationBuilder.DropColumn(name: "GrossAmountCents", table: "TenantOnboardings");

            migrationBuilder.DropColumn(name: "NetAmountCents", table: "TenantOnboardings");

            migrationBuilder.DropColumn(name: "ReferralAttributionId", table: "TenantOnboardings");

            migrationBuilder.DropColumn(name: "TotalDiscountCents", table: "TenantOnboardings");
        }
    }
}
