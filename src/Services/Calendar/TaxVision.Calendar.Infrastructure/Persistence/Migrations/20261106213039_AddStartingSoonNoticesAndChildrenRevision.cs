using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Calendar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStartingSoonNoticesAndChildrenRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ChildrenRevision",
                table: "Appointments",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.CreateTable(
                name: "AppointmentStartingSoonNotices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurrenceTargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentStartingSoonNotices", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "UX_AppointmentStartingSoonNotices_Occurrence",
                table: "AppointmentStartingSoonNotices",
                columns: new[] { "TenantId", "OccurrenceTargetId", "StartUtc" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AppointmentStartingSoonNotices");

            migrationBuilder.DropColumn(name: "ChildrenRevision", table: "Appointments");
        }
    }
}
