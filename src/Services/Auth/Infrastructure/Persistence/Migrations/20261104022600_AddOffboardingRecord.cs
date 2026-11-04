using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOffboardingRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OffboardedByUserId",
                table: "OffboardingRuns",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "OffboardingRuns",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "ReasonNote",
                table: "OffboardingRuns",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "SuccessorUserId",
                table: "OffboardingRuns",
                type: "uniqueidentifier",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "OffboardedByUserId", table: "OffboardingRuns");

            migrationBuilder.DropColumn(name: "Reason", table: "OffboardingRuns");

            migrationBuilder.DropColumn(name: "ReasonNote", table: "OffboardingRuns");

            migrationBuilder.DropColumn(name: "SuccessorUserId", table: "OffboardingRuns");
        }
    }
}
