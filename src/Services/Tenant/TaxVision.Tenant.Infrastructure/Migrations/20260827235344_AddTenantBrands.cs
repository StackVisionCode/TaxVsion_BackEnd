using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tenant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantBrands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantBrands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Surface = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantBrands", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "TenantBrandAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantBrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Width = table.Column<int>(type: "int", nullable: true),
                    Height = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantBrandAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantBrandAssets_TenantBrands_TenantBrandId",
                        column: x => x.TenantBrandId,
                        principalTable: "TenantBrands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "TenantBrandColors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantBrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Token = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    HexValue = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantBrandColors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantBrandColors_TenantBrands_TenantBrandId",
                        column: x => x.TenantBrandId,
                        principalTable: "TenantBrands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantBrandAssets_TenantBrandId_Key",
                table: "TenantBrandAssets",
                columns: new[] { "TenantBrandId", "Key" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantBrandColors_TenantBrandId_Token",
                table: "TenantBrandColors",
                columns: new[] { "TenantBrandId", "Token" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantBrands_TenantId_Surface",
                table: "TenantBrands",
                columns: new[] { "TenantId", "Surface" },
                unique: true
            );

            // Marca del SISTEMA (nivel 2 de la cascada de defaults): filas del tenant de plataforma
            // 8f58a521-... con paleta oficial TaxProffice. Un tenant sin personalizar cae a estos
            // valores. CRM y portal comparten paleta hoy, pero son filas independientes: el
            // PlatformAdmin podrá darles colores/logos distintos por superficie. Sin assets sembrados:
            // el logo/favicon del sistema los sube el PlatformAdmin (o se resuelven a la constante
            // compilada, nivel 3). Guids literales fijos — una migración es un registro histórico.
            var platformTenantId = new Guid("8f58a521-4c25-4d91-9f4e-7ad5df14c001");
            var crmBrandId = new Guid("b1000000-0000-0000-0000-000000000001");
            var portalBrandId = new Guid("b1000000-0000-0000-0000-000000000002");
            var seededAtUtc = new DateTime(2026, 8, 27, 0, 0, 0, DateTimeKind.Utc);

            migrationBuilder.InsertData(
                table: "TenantBrands",
                columns: new[] { "Id", "Surface", "CreatedAtUtc", "UpdatedAtUtc", "TenantId" },
                values: new object[,]
                {
                    { crmBrandId, "Crm", seededAtUtc, seededAtUtc, platformTenantId },
                    { portalBrandId, "Portal", seededAtUtc, seededAtUtc, platformTenantId },
                }
            );

            migrationBuilder.InsertData(
                table: "TenantBrandColors",
                columns: new[] { "Id", "TenantBrandId", "Token", "HexValue", "UpdatedAtUtc", "TenantId" },
                values: new object[,]
                {
                    {
                        new Guid("b2000000-0000-0000-0000-000000000001"),
                        crmBrandId,
                        "Primary",
                        "#1E466B",
                        seededAtUtc,
                        platformTenantId,
                    },
                    {
                        new Guid("b2000000-0000-0000-0000-000000000002"),
                        crmBrandId,
                        "Accent",
                        "#67BAF4",
                        seededAtUtc,
                        platformTenantId,
                    },
                    {
                        new Guid("b2000000-0000-0000-0000-000000000003"),
                        portalBrandId,
                        "Primary",
                        "#1E466B",
                        seededAtUtc,
                        platformTenantId,
                    },
                    {
                        new Guid("b2000000-0000-0000-0000-000000000004"),
                        portalBrandId,
                        "Accent",
                        "#67BAF4",
                        seededAtUtc,
                        platformTenantId,
                    },
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TenantBrandAssets");

            migrationBuilder.DropTable(name: "TenantBrandColors");

            migrationBuilder.DropTable(name: "TenantBrands");
        }
    }
}
