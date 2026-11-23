using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformSessionPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformSessionPolicies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    IdleTimeoutMinutes = table.Column<int>(type: "int", nullable: false),
                    WarningSeconds = table.Column<int>(type: "int", nullable: false),
                    AbsoluteLifetimeHours = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformSessionPolicies", x => x.Id);
                }
            );

            // La fila única con los valores por defecto: 30 min de inactividad, 60 s de aviso y 12 h de máximo.
            migrationBuilder.InsertData(
                table: "PlatformSessionPolicies",
                columns: new[]
                {
                    "Id",
                    "IdleTimeoutMinutes",
                    "WarningSeconds",
                    "AbsoluteLifetimeHours",
                    "UpdatedAtUtc",
                    "UpdatedBy",
                },
                values: new object[]
                {
                    "global",
                    30,
                    60,
                    12,
                    new DateTime(2026, 11, 22, 0, 0, 0, DateTimeKind.Utc),
                    Guid.Empty,
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PlatformSessionPolicies");
        }
    }
}
