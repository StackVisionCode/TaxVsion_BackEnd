using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormRequestProfileUpdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PrefilledComponentIds",
                table: "FormRequests",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]"
            );

            migrationBuilder.AddColumn<Guid>(
                name: "ProfileUpdateCommandId",
                table: "FormRequests",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "ProfileUpdateOutcomes",
                table: "FormRequests",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]"
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "ProfileUpdateRequestedAtUtc",
                table: "FormRequests",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "ProfileUpdateResolvedAtUtc",
                table: "FormRequests",
                type: "datetime2",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PrefilledComponentIds", table: "FormRequests");

            migrationBuilder.DropColumn(name: "ProfileUpdateCommandId", table: "FormRequests");

            migrationBuilder.DropColumn(name: "ProfileUpdateOutcomes", table: "FormRequests");

            migrationBuilder.DropColumn(name: "ProfileUpdateRequestedAtUtc", table: "FormRequests");

            migrationBuilder.DropColumn(name: "ProfileUpdateResolvedAtUtc", table: "FormRequests");
        }
    }
}
