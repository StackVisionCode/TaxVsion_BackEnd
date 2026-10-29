using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormActionRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "OwnerReassignedAtUtc",
                table: "FormAttachments",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "OwnerReassignment",
                table: "FormAttachments",
                type: "nvarchar(24)",
                maxLength: 24,
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "FormActionRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionNumber = table.Column<int>(type: "int", nullable: false),
                    ActionKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActionType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CommandId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Attempt = table.Column<int>(type: "int", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResultRefKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ResultRefId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExistingCustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ErrorDetail = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Warnings = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RetriedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormActionRuns", x => x.Id);
                    table.CheckConstraint(
                        "CK_FormActionRuns_Command",
                        "([Status] = 5 AND [CommandId] IS NULL) OR ([Status] = 4) OR ([Status] IN (1, 2, 3) AND [CommandId] IS NOT NULL)"
                    );
                    table.ForeignKey(
                        name: "FK_FormActionRuns_FormRequests_FormRequestId",
                        column: x => x.FormRequestId,
                        principalTable: "FormRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormActionRuns_Status_RequestedAtUtc",
                table: "FormActionRuns",
                columns: new[] { "Status", "RequestedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "UX_FormActionRuns_Request_Key_Attempt",
                table: "FormActionRuns",
                columns: new[] { "FormRequestId", "IdempotencyKey", "Attempt" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "UX_FormActionRuns_Tenant_Command",
                table: "FormActionRuns",
                columns: new[] { "TenantId", "CommandId" },
                unique: true,
                filter: "[CommandId] IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FormActionRuns");

            migrationBuilder.DropColumn(name: "OwnerReassignedAtUtc", table: "FormAttachments");

            migrationBuilder.DropColumn(name: "OwnerReassignment", table: "FormAttachments");
        }
    }
}
