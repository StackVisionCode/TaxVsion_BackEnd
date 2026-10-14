using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerDocumentAccessRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerAccessRuleAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResourceType = table.Column<int>(type: "int", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Capability = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAccessRuleAudits", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "CustomerDocumentAccessRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResourceType = table.Column<int>(type: "int", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Capability = table.Column<int>(type: "int", nullable: false),
                    SetByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SetAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerDocumentAccessRules", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAccessRuleAudits_Tenant_Customer_OccurredAt",
                table: "CustomerAccessRuleAudits",
                columns: new[] { "TenantId", "CustomerId", "OccurredAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAccessRuleAudits_Tenant_OccurredAt",
                table: "CustomerAccessRuleAudits",
                columns: new[] { "TenantId", "OccurredAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDocumentAccessRules_Customer_Resource_Capability",
                table: "CustomerDocumentAccessRules",
                columns: new[] { "TenantId", "CustomerId", "ResourceType", "ResourceId", "Capability" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDocumentAccessRules_Tenant_Customer",
                table: "CustomerDocumentAccessRules",
                columns: new[] { "TenantId", "CustomerId" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CustomerAccessRuleAudits");

            migrationBuilder.DropTable(name: "CustomerDocumentAccessRules");
        }
    }
}
