using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanRateLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlanRateLimits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    MultiplierOverride = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    HardOverridePerMinute = table.Column<int>(type: "int", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanRateLimits", x => x.Id);
                }
            );

            migrationBuilder.InsertData(
                table: "PlanRateLimits",
                columns: new[] { "Id", "Category", "HardOverridePerMinute", "MultiplierOverride", "PlanCode" },
                values: new object[,]
                {
                    { new Guid("c3000000-0000-0000-0000-000000000000"), "F", null, 1.0m, "starter" },
                    { new Guid("c3000000-0000-0000-0000-000000000001"), "G", null, 1.0m, "starter" },
                    { new Guid("c3000000-0000-0000-0000-000000000002"), "H", null, 1.0m, "starter" },
                    { new Guid("c3000000-0000-0000-0000-000000000003"), "I", null, 1.0m, "starter" },
                    { new Guid("c3000000-0000-0000-0000-000000000004"), "J", null, 1.0m, "starter" },
                    { new Guid("c3000000-0000-0000-0000-000000000005"), "K", null, 1.0m, "starter" },
                    { new Guid("c3000000-0000-0000-0000-000000000006"), "L", null, 1.0m, "starter" },
                    { new Guid("c3000000-0000-0000-0000-000000000007"), "M", null, 1.0m, "starter" },
                    { new Guid("c3000000-0000-0000-0000-000000000008"), "N", null, 1.0m, "starter" },
                    { new Guid("c3000000-0000-0000-0000-000000000009"), "O", null, 1.0m, "starter" },
                    { new Guid("c3000000-0000-0000-0000-000000000010"), "F", null, 3.0m, "pro" },
                    { new Guid("c3000000-0000-0000-0000-000000000011"), "G", null, 3.0m, "pro" },
                    { new Guid("c3000000-0000-0000-0000-000000000012"), "H", null, 3.0m, "pro" },
                    { new Guid("c3000000-0000-0000-0000-000000000013"), "I", null, 5.0m, "pro" },
                    { new Guid("c3000000-0000-0000-0000-000000000014"), "J", null, 5.0m, "pro" },
                    { new Guid("c3000000-0000-0000-0000-000000000015"), "K", null, 3.0m, "pro" },
                    { new Guid("c3000000-0000-0000-0000-000000000016"), "L", null, 3.0m, "pro" },
                    { new Guid("c3000000-0000-0000-0000-000000000017"), "M", null, 1.0m, "pro" },
                    { new Guid("c3000000-0000-0000-0000-000000000018"), "N", null, 1.0m, "pro" },
                    { new Guid("c3000000-0000-0000-0000-000000000019"), "O", null, 3.0m, "pro" },
                    { new Guid("c3000000-0000-0000-0000-000000000020"), "F", null, 10.0m, "enterprise" },
                    { new Guid("c3000000-0000-0000-0000-000000000021"), "G", null, 10.0m, "enterprise" },
                    { new Guid("c3000000-0000-0000-0000-000000000022"), "H", null, 15.0m, "enterprise" },
                    { new Guid("c3000000-0000-0000-0000-000000000023"), "I", null, 10.0m, "enterprise" },
                    { new Guid("c3000000-0000-0000-0000-000000000024"), "J", null, 10.0m, "enterprise" },
                    { new Guid("c3000000-0000-0000-0000-000000000025"), "K", null, 20.0m, "enterprise" },
                    { new Guid("c3000000-0000-0000-0000-000000000026"), "L", null, 10.0m, "enterprise" },
                    { new Guid("c3000000-0000-0000-0000-000000000027"), "M", null, 1.0m, "enterprise" },
                    { new Guid("c3000000-0000-0000-0000-000000000028"), "N", null, 1.0m, "enterprise" },
                    { new Guid("c3000000-0000-0000-0000-000000000029"), "O", null, 10.0m, "enterprise" },
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_PlanRateLimits_PlanCode_Category",
                table: "PlanRateLimits",
                columns: new[] { "PlanCode", "Category" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PlanRateLimits");
        }
    }
}
