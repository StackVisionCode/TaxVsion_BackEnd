using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeriesSweepTaxYearAndTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaterializationAttempts",
                table: "TaskSeries",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "NextMaterializationAtUtc",
                table: "TaskSeries",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(name: "TaxYearDueYear", table: "TaskSeries", type: "int", nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TemplateId",
                table: "TaskSeries",
                type: "uniqueidentifier",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "MaterializationAttempts", table: "TaskSeries");

            migrationBuilder.DropColumn(name: "NextMaterializationAtUtc", table: "TaskSeries");

            migrationBuilder.DropColumn(name: "TaxYearDueYear", table: "TaskSeries");

            migrationBuilder.DropColumn(name: "TemplateId", table: "TaskSeries");
        }
    }
}
