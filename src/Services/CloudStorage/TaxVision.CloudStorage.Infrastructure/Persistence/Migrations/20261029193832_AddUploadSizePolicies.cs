using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadSizePolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UploadSizePolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    FolderType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PlanCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MaxFileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadSizePolicies", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "UploadSizePolicyAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    FolderType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PlanCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OldMaxFileSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    NewMaxFileSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadSizePolicyAudits", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_UploadSizePolicies_Scope_FolderType_PlanCode_TenantId",
                table: "UploadSizePolicies",
                columns: new[] { "Scope", "FolderType", "PlanCode", "TenantId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_UploadSizePolicyAudits_OccurredAt",
                table: "UploadSizePolicyAudits",
                column: "OccurredAtUtc"
            );

            // Siembra inicial (D-37): solo el tope Global de Recordings en 2 GB; el resto de carpetas
            // sigue con el fallback de código hasta que PlatformAdmin las fije. Fuera de HasData a
            // propósito para que el modelo no la "reconcilie" en migraciones futuras.
            migrationBuilder.InsertData(
                table: "UploadSizePolicies",
                columns: new[]
                {
                    "Id",
                    "Scope",
                    "FolderType",
                    "PlanCode",
                    "TenantId",
                    "MaxFileSizeBytes",
                    "UpdatedByUserId",
                    "UpdatedAtUtc",
                },
                values: new object[]
                {
                    new Guid("c1000000-0000-0000-0000-000000000001"),
                    "Global",
                    "Recordings",
                    null,
                    null,
                    2147483648L,
                    Guid.Empty,
                    new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc),
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "UploadSizePolicies",
                keyColumn: "Id",
                keyValue: new Guid("c1000000-0000-0000-0000-000000000001")
            );

            migrationBuilder.DropTable(name: "UploadSizePolicies");

            migrationBuilder.DropTable(name: "UploadSizePolicyAudits");
        }
    }
}
