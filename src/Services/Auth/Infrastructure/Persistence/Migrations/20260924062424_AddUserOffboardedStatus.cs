using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserOffboardedStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RemovedAtUtc",
                table: "Users",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Users",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Active"
            );

            // Backfill: los usuarios ya desactivados quedan en estado Deactivated.
            migrationBuilder.Sql("UPDATE [Users] SET [Status] = 'Deactivated' WHERE [IsActive] = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "RemovedAtUtc", table: "Users");

            migrationBuilder.DropColumn(name: "Status", table: "Users");
        }
    }
}
