using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tenant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspacePreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkspacePreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PreferenceKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspacePreferences", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_WorkspacePreferences_TenantId_UserId_PreferenceKey",
                table: "WorkspacePreferences",
                columns: new[] { "TenantId", "UserId", "PreferenceKey" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "WorkspacePreferences");
        }
    }
}
