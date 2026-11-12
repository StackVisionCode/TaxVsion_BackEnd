using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRateLimitDefaultProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RateLimitDefaultProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RateLimitDefaultProfiles", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "RateLimitDefaultProfileEntries",
                columns: table => new
                {
                    Category = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Multiplier = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RateLimitDefaultProfileEntries", x => new { x.ProfileId, x.Category });
                    table.ForeignKey(
                        name: "FK_RateLimitDefaultProfileEntries_RateLimitDefaultProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "RateLimitDefaultProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.InsertData(
                table: "RateLimitDefaultProfiles",
                columns: new[] { "Id", "UpdatedAtUtc", "UpdatedBy" },
                values: new object[]
                {
                    new Guid("c4000000-0000-0000-0000-000000000001"),
                    new DateTime(2026, 11, 11, 0, 0, 0, 0, DateTimeKind.Utc),
                    new Guid("00000000-0000-0000-0000-000000000000"),
                }
            );

            migrationBuilder.InsertData(
                table: "RateLimitDefaultProfileEntries",
                columns: new[] { "Category", "ProfileId", "Multiplier" },
                values: new object[,]
                {
                    { "F", new Guid("c4000000-0000-0000-0000-000000000001"), 10m },
                    { "G", new Guid("c4000000-0000-0000-0000-000000000001"), 10m },
                    { "H", new Guid("c4000000-0000-0000-0000-000000000001"), 10m },
                    { "I", new Guid("c4000000-0000-0000-0000-000000000001"), 10m },
                    { "J", new Guid("c4000000-0000-0000-0000-000000000001"), 10m },
                    { "K", new Guid("c4000000-0000-0000-0000-000000000001"), 20m },
                    { "L", new Guid("c4000000-0000-0000-0000-000000000001"), 10m },
                    { "M", new Guid("c4000000-0000-0000-0000-000000000001"), 1m },
                    { "N", new Guid("c4000000-0000-0000-0000-000000000001"), 1m },
                    { "O", new Guid("c4000000-0000-0000-0000-000000000001"), 10m },
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "RateLimitDefaultProfileEntries");

            migrationBuilder.DropTable(name: "RateLimitDefaultProfiles");
        }
    }
}
