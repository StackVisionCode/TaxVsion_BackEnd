using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantDomains : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantDomains",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DomainType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Host = table.Column<string>(type: "nvarchar(253)", maxLength: 253, nullable: false),
                    SubdomainSlug = table.Column<string>(type: "nvarchar(63)", maxLength: 63, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    CloudflareCustomHostnameId = table.Column<string>(
                        type: "nvarchar(64)",
                        maxLength: 64,
                        nullable: true
                    ),
                    VerificationMethod = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    VerifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantDomains", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "TenantSubdomainReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubdomainSlug = table.Column<string>(type: "nvarchar(63)", maxLength: 63, nullable: false),
                    ReservedByEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSubdomainReservations", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantDomains_Host",
                table: "TenantDomains",
                column: "Host",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantDomains_SubdomainSlug",
                table: "TenantDomains",
                column: "SubdomainSlug",
                unique: true,
                filter: "[SubdomainSlug] IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantDomains_TenantId_IsPrimary",
                table: "TenantDomains",
                columns: new[] { "TenantId", "IsPrimary" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantDomains_TenantId_Status",
                table: "TenantDomains",
                columns: new[] { "TenantId", "Status" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantSubdomainReservations_SubdomainSlug_ConsumedAtUtc_ExpiresAtUtc",
                table: "TenantSubdomainReservations",
                columns: new[] { "SubdomainSlug", "ConsumedAtUtc", "ExpiresAtUtc" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TenantDomains");

            migrationBuilder.DropTable(name: "TenantSubdomainReservations");
        }
    }
}
