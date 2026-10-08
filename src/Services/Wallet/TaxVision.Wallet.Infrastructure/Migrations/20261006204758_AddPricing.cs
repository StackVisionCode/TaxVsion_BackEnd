using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Wallet.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PriceBookVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceBookVersions", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "PriceRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PriceBookVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    UnitPriceMicros = table.Column<long>(type: "bigint", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PriceRules_PriceBookVersions_PriceBookVersionId",
                        column: x => x.PriceBookVersionId,
                        principalTable: "PriceBookVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.InsertData(
                table: "PriceBookVersions",
                columns: new[] { "Id", "CreatedAtUtc", "CreatedByUserId", "EffectiveFromUtc", "Version" },
                values: new object[]
                {
                    new Guid("b2000000-0000-0000-0000-000000000001"),
                    new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc),
                    new Guid("00000000-0000-0000-0000-000000000000"),
                    new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc),
                    1,
                }
            );

            migrationBuilder.InsertData(
                table: "PriceRules",
                columns: new[] { "Id", "Channel", "PriceBookVersionId", "UnitPriceMicros" },
                values: new object[,]
                {
                    {
                        new Guid("b2000000-0000-0000-0001-000000000001"),
                        "Email",
                        new Guid("b2000000-0000-0000-0000-000000000001"),
                        10000L,
                    },
                    {
                        new Guid("b2000000-0000-0000-0001-000000000002"),
                        "Sms",
                        new Guid("b2000000-0000-0000-0000-000000000001"),
                        50000L,
                    },
                    {
                        new Guid("b2000000-0000-0000-0001-000000000003"),
                        "Push",
                        new Guid("b2000000-0000-0000-0000-000000000001"),
                        10000L,
                    },
                    {
                        new Guid("b2000000-0000-0000-0001-000000000004"),
                        "WhatsApp",
                        new Guid("b2000000-0000-0000-0000-000000000001"),
                        50000L,
                    },
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_PriceBookVersions_Version",
                table: "PriceBookVersions",
                column: "Version",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_PriceRules_PriceBookVersionId_Channel",
                table: "PriceRules",
                columns: new[] { "PriceBookVersionId", "Channel" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PriceRules");

            migrationBuilder.DropTable(name: "PriceBookVersions");
        }
    }
}
