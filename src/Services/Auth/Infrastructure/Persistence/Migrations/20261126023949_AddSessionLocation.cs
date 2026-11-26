using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LocationCity",
                table: "UserSessions",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "LocationCountryCode",
                table: "UserSessions",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: true
            );

            migrationBuilder.AddColumn<double>(
                name: "LocationLatitude",
                table: "UserSessions",
                type: "float",
                nullable: true
            );

            migrationBuilder.AddColumn<double>(
                name: "LocationLongitude",
                table: "UserSessions",
                type: "float",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "LocationRegion",
                table: "UserSessions",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "LocationResolvedAtUtc",
                table: "UserSessions",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "LocationSource",
                table: "UserSessions",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "LocationCity", table: "UserSessions");

            migrationBuilder.DropColumn(name: "LocationCountryCode", table: "UserSessions");

            migrationBuilder.DropColumn(name: "LocationLatitude", table: "UserSessions");

            migrationBuilder.DropColumn(name: "LocationLongitude", table: "UserSessions");

            migrationBuilder.DropColumn(name: "LocationRegion", table: "UserSessions");

            migrationBuilder.DropColumn(name: "LocationResolvedAtUtc", table: "UserSessions");

            migrationBuilder.DropColumn(name: "LocationSource", table: "UserSessions");
        }
    }
}
