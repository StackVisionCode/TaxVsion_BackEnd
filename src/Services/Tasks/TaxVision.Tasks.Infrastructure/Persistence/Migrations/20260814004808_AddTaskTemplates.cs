using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaskTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskTemplates", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "TaskTemplateSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    EstimatedHours = table.Column<decimal>(
                        type: "decimal(6,2)",
                        precision: 6,
                        scale: 2,
                        nullable: true
                    ),
                    DueOffsetDays = table.Column<int>(type: "int", nullable: false),
                    IsStatutory = table.Column<bool>(type: "bit", nullable: false),
                    DependsOnStepOrder = table.Column<int>(type: "int", nullable: true),
                    ParentStepOrder = table.Column<int>(type: "int", nullable: true),
                    SuggestedRoleName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskTemplateSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskTemplateSteps_TaskTemplates_TaskTemplateId",
                        column: x => x.TaskTemplateId,
                        principalTable: "TaskTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TaskTemplates_TenantId_IsActive",
                table: "TaskTemplates",
                columns: new[] { "TenantId", "IsActive" }
            );

            migrationBuilder.CreateIndex(
                name: "UX_TaskTemplateSteps_TemplateId_Order",
                table: "TaskTemplateSteps",
                columns: new[] { "TaskTemplateId", "Order" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TaskTemplateSteps");

            migrationBuilder.DropTable(name: "TaskTemplates");
        }
    }
}
