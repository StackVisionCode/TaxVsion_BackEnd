using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomerDirectoryStatusChangedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "StatusChangedAtUtc",
                table: "CustomerDirectoryEntries",
                type: "datetime2",
                nullable: true
            );

            // Las filas existentes ordenan su estado desde la hora que ya tenían (era una sola para todo).
            migrationBuilder.Sql(
                "EXEC sp_executesql N'UPDATE CustomerDirectoryEntries SET StatusChangedAtUtc = UpdatedAtUtc WHERE StatusChangedAtUtc IS NULL';"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "StatusChangedAtUtc", table: "CustomerDirectoryEntries");
        }
    }
}
