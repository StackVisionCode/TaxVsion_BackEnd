using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanModulesCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlanModules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanModules", x => x.Id);
                }
            );

            migrationBuilder.InsertData(
                table: "PlanModules",
                columns: new[] { "Id", "Code", "Description", "DisplayName" },
                values: new object[,]
                {
                    {
                        new Guid("b2000000-0000-0000-0000-000000000001"),
                        "customers",
                        "Customer directory, imports and assignments.",
                        "Customers",
                    },
                    {
                        new Guid("b2000000-0000-0000-0000-000000000002"),
                        "signatures",
                        "e-Signature requests, signer flow and audit.",
                        "Signatures",
                    },
                    {
                        new Guid("b2000000-0000-0000-0000-000000000003"),
                        "documents",
                        "CloudStorage, Scribe templates and shared documents.",
                        "Documents",
                    },
                    {
                        new Guid("b2000000-0000-0000-0000-000000000004"),
                        "planner",
                        "Calendar, reminders, tasks and notes.",
                        "Planner",
                    },
                    {
                        new Guid("b2000000-0000-0000-0000-000000000005"),
                        "email",
                        "Correspondence inbox, connectors and postmaster.",
                        "Email",
                    },
                    {
                        new Guid("b2000000-0000-0000-0000-000000000006"),
                        "meetings",
                        "Scheduled meetings separate from day-to-day chat (sold apart).",
                        "Meetings",
                    },
                    {
                        new Guid("b2000000-0000-0000-0000-000000000007"),
                        "comms",
                        "Chat, voice and video between tenant members and customers.",
                        "Communication",
                    },
                    {
                        new Guid("b2000000-0000-0000-0000-000000000008"),
                        "campaigns",
                        "Outbound email campaigns and senders.",
                        "Campaigns",
                    },
                    {
                        new Guid("b2000000-0000-0000-0000-000000000009"),
                        "wallet",
                        "Tenant wallet balance, top-ups and spend audit.",
                        "Wallet",
                    },
                    {
                        new Guid("b2000000-0000-0000-0000-00000000000a"),
                        "reports",
                        "Operational reports (feature not built; reserved for F3+).",
                        "Reports",
                    },
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_PlanModules_Code",
                table: "PlanModules",
                column: "Code",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PlanModules");
        }
    }
}
