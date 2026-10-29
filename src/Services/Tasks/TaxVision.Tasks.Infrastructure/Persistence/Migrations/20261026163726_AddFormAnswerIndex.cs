using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormAnswerIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FormAnswerIndexEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RevisionNumber = table.Column<int>(type: "int", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RowIndex = table.Column<int>(type: "int", nullable: true),
                    ValueText = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ValueNumber = table.Column<decimal>(type: "decimal(19,6)", nullable: true),
                    ValueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormAnswerIndexEntries", x => x.Id);
                    table.CheckConstraint(
                        "CK_FormAnswerIndexEntries_CustomerOrParticipant",
                        "[CustomerId] IS NOT NULL OR [ParticipantId] IS NOT NULL"
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormAnswerIndexEntries_TenantId_Request",
                table: "FormAnswerIndexEntries",
                columns: new[] { "TenantId", "FormRequestId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormAnswerIndexEntries_TenantId_Template_Key_Value",
                table: "FormAnswerIndexEntries",
                columns: new[] { "TenantId", "FormTemplateId", "Key", "ValueText" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FormAnswerIndexEntries");
        }
    }
}
