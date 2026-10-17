using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateUnifiedModuleCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Modules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AddonPriceMonthlyUsd = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    AddonPriceYearlyUsd = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Modules", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(name: "IX_Modules_Code", table: "Modules", column: "Code", unique: true);

            migrationBuilder.InsertData(
                table: "Modules",
                columns:
                [
                    "Id",
                    "Code",
                    "Name",
                    "Description",
                    "Category",
                    "AddonPriceMonthlyUsd",
                    "AddonPriceYearlyUsd",
                ],
                values: new object[,]
                {
                    {
                        new Guid("b2000000-0000-0000-0000-000000000001"),
                        "customers",
                        "Customers",
                        "Customer directory, imports and assignments.",
                        "Core",
                        null,
                        null,
                    },
                    {
                        new Guid("b2000000-0000-0000-0000-000000000002"),
                        "signatures",
                        "Signatures",
                        "Electronic signature requests and audit.",
                        "Core",
                        null,
                        null,
                    },
                    {
                        new Guid("b2000000-0000-0000-0000-000000000003"),
                        "documents",
                        "Documents",
                        "Storage, templates and shared documents.",
                        "Core",
                        null,
                        null,
                    },
                    {
                        new Guid("b2000000-0000-0000-0000-000000000004"),
                        "planner",
                        "Planner",
                        "Calendar, reminders, tasks and notes.",
                        "Core",
                        null,
                        null,
                    },
                    {
                        new Guid("b2000000-0000-0000-0000-000000000005"),
                        "email",
                        "Email",
                        "Correspondence inbox, connectors and postmaster.",
                        "Commercial",
                        29m,
                        290m,
                    },
                    {
                        new Guid("b2000000-0000-0000-0000-000000000006"),
                        "meetings",
                        "Meetings",
                        "Scheduled meetings for office staff and customers.",
                        "Commercial",
                        29m,
                        290m,
                    },
                    {
                        new Guid("b2000000-0000-0000-0000-000000000007"),
                        "comms",
                        "Communication",
                        "Chat, voice and video communication.",
                        "Core",
                        null,
                        null,
                    },
                    {
                        new Guid("b2000000-0000-0000-0000-000000000008"),
                        "campaigns",
                        "Campaigns",
                        "Outbound email campaigns and senders.",
                        "Commercial",
                        29m,
                        290m,
                    },
                    {
                        new Guid("b2000000-0000-0000-0000-000000000009"),
                        "wallet",
                        "Wallet",
                        "Tenant wallet balance, top-ups and spend audit.",
                        "Core",
                        null,
                        null,
                    },
                    {
                        new Guid("b2000000-0000-0000-0000-00000000000a"),
                        "reports",
                        "Reports",
                        "Operational reports reserved for a future release.",
                        "Commercial",
                        null,
                        null,
                    },
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Modules");
        }
    }
}
