using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Growth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReferredOffices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReferredOffices",
                schema: "referrals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OnboardingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameUpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ProfileVersion = table.Column<long>(type: "bigint", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferredOffices", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ReferredOffices_OnboardingId",
                schema: "referrals",
                table: "ReferredOffices",
                column: "OnboardingId",
                filter: "[OnboardingId] IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "UX_ReferredOffices_TenantId",
                schema: "referrals",
                table: "ReferredOffices",
                column: "TenantId",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ReferredOffices", schema: "referrals");
        }
    }
}
