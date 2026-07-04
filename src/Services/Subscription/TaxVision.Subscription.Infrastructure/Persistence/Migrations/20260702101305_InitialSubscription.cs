using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSubscription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    MonthlyPriceUsd = table.Column<decimal>(
                        type: "decimal(10,2)",
                        precision: 10,
                        scale: 2,
                        nullable: false
                    ),
                    MaxUsers = table.Column<int>(type: "int", nullable: false),
                    MaxPendingInvitations = table.Column<int>(type: "int", nullable: false),
                    StorageQuotaBytes = table.Column<long>(type: "bigint", nullable: false),
                    EnabledModulesJson = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plans", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ExtraSeats = table.Column<int>(type: "int", nullable: false),
                    TrialEndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CurrentPeriodStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CurrentPeriodEndUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SuspensionReason = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subscriptions_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.InsertData(
                table: "Plans",
                columns: new[]
                {
                    "Id",
                    "Code",
                    "Description",
                    "EnabledModulesJson",
                    "IsActive",
                    "MaxPendingInvitations",
                    "MaxUsers",
                    "MonthlyPriceUsd",
                    "Name",
                    "SortOrder",
                    "StorageQuotaBytes",
                },
                values: new object[,]
                {
                    {
                        new Guid("b1000000-0000-0000-0000-000000000001"),
                        "starter",
                        "Para oficinas que están empezando: 3 usuarios, clientes, firmas y documentos.",
                        "[\"customers\",\"signatures\",\"documents\",\"planner\"]",
                        true,
                        5,
                        3,
                        49m,
                        "Starter",
                        1,
                        10737418240L,
                    },
                    {
                        new Guid("b1000000-0000-0000-0000-000000000002"),
                        "pro",
                        "Para oficinas en crecimiento: 10 usuarios, correo, comunicación y campañas.",
                        "[\"customers\",\"signatures\",\"documents\",\"planner\",\"email\",\"comms\",\"campaigns\",\"reports\"]",
                        true,
                        15,
                        10,
                        129m,
                        "Pro",
                        2,
                        53687091200L,
                    },
                    {
                        new Guid("b1000000-0000-0000-0000-000000000003"),
                        "enterprise",
                        "Para multiservices con equipos grandes: 25 usuarios y todos los módulos.",
                        "[\"customers\",\"signatures\",\"documents\",\"planner\",\"email\",\"comms\",\"campaigns\",\"reports\",\"marketing\",\"builder\",\"irs\",\"miles\"]",
                        true,
                        40,
                        25,
                        299m,
                        "Enterprise",
                        3,
                        214748364800L,
                    },
                }
            );

            migrationBuilder.CreateIndex(name: "IX_Plans_Code", table: "Plans", column: "Code", unique: true);

            migrationBuilder.CreateIndex(name: "IX_Subscriptions_PlanId", table: "Subscriptions", column: "PlanId");

            migrationBuilder.CreateIndex(name: "IX_Subscriptions_Status", table: "Subscriptions", column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_TenantId",
                table: "Subscriptions",
                column: "TenantId",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Subscriptions");

            migrationBuilder.DropTable(name: "Plans");
        }
    }
}
