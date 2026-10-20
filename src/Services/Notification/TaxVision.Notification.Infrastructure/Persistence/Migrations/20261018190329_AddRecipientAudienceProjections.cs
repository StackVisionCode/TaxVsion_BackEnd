using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Notification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecipientAudienceProjections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerPortalAccountProjections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerPortalAccountProjections", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "TenantOwnerProjections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RegisteredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantOwnerProjections", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPortalAccountProjections_TenantId_CustomerId",
                table: "CustomerPortalAccountProjections",
                columns: new[] { "TenantId", "CustomerId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPortalAccountProjections_TenantId_IsActive",
                table: "CustomerPortalAccountProjections",
                columns: new[] { "TenantId", "IsActive" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantOwnerProjections_TenantId_IsActive_RegisteredAtUtc",
                table: "TenantOwnerProjections",
                columns: new[] { "TenantId", "IsActive", "RegisteredAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantOwnerProjections_TenantId_UserId",
                table: "TenantOwnerProjections",
                columns: new[] { "TenantId", "UserId" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CustomerPortalAccountProjections");

            migrationBuilder.DropTable(name: "TenantOwnerProjections");
        }
    }
}
