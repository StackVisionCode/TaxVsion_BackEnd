using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCivilDueDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(name: "ClientDueOn", table: "Tasks", type: "date", nullable: true);

            migrationBuilder.AddColumn<DateOnly>(name: "DueOn", table: "ClientRequests", type: "date", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ClientDueOn", table: "Tasks");

            migrationBuilder.DropColumn(name: "DueOn", table: "ClientRequests");
        }
    }
}
