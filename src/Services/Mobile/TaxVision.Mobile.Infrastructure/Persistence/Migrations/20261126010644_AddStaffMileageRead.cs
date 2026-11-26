using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Mobile.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffMileageRead : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerAssignmentProjections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAssignmentProjections", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "MobileStaffAccessLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TripId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobileStaffAccessLogs", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "MobileStandardMileageRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    RatePerMile = table.Column<decimal>(
                        type: "decimal(10,4)",
                        precision: 10,
                        scale: 4,
                        nullable: false
                    ),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobileStandardMileageRates", x => x.Id);
                    table.CheckConstraint(
                        "CK_MobileStandardMileageRates_Rate",
                        "[RatePerMile] >= 0 AND [RatePerMile] <= 1000"
                    );
                }
            );

            migrationBuilder.InsertData(
                table: "MobileStandardMileageRates",
                columns: new[] { "Id", "Currency", "EffectiveFrom", "Purpose", "RatePerMile", "Source" },
                values: new object[,]
                {
                    {
                        new Guid("0384a26b-a345-09de-6960-5492d59934a9"),
                        "USD",
                        new DateOnly(2023, 1, 1),
                        "charity",
                        0.14m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("13d78ee2-2f3d-93dc-ad14-c2a1477cd06a"),
                        "USD",
                        new DateOnly(2025, 1, 1),
                        "moving",
                        0.21m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("145b5c45-2fd8-71c4-a240-2158e24072ab"),
                        "USD",
                        new DateOnly(2026, 7, 1),
                        "charity",
                        0.14m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("17e78014-5c0b-e475-2515-16795f77244b"),
                        "USD",
                        new DateOnly(2023, 1, 1),
                        "medical",
                        0.22m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("28d06d6d-ce92-04d6-b873-90eb8c609e45"),
                        "USD",
                        new DateOnly(2026, 7, 1),
                        "moving",
                        0.235m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("37b9e110-65f5-411e-a8e1-92da1b236e82"),
                        "USD",
                        new DateOnly(2023, 1, 1),
                        "business",
                        0.655m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("407ef66c-e907-b246-5161-03111869a28d"),
                        "USD",
                        new DateOnly(2026, 7, 1),
                        "business",
                        0.76m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("4aa2b5d6-93b5-d0a1-18cf-4ffcbac3c4e8"),
                        "USD",
                        new DateOnly(2024, 1, 1),
                        "medical",
                        0.21m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("4e0e4bfc-c3e9-0941-10eb-a0fc0b90f487"),
                        "USD",
                        new DateOnly(2021, 1, 1),
                        "medical",
                        0.16m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("4f047b57-d29a-4e22-e6b7-42526e2493e1"),
                        "USD",
                        new DateOnly(2020, 1, 1),
                        "charity",
                        0.14m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("5acf69f4-b0fd-07d5-76f7-e33eb971d46c"),
                        "USD",
                        new DateOnly(2026, 7, 1),
                        "medical",
                        0.235m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("5f3c5738-0f73-f3dc-657d-9245d5d670ed"),
                        "USD",
                        new DateOnly(2022, 1, 1),
                        "medical",
                        0.18m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("67d9abc7-2252-5e25-c12e-0a78b7043342"),
                        "USD",
                        new DateOnly(2025, 1, 1),
                        "charity",
                        0.14m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("68b0d5e9-adca-e006-0fb6-9639379429e8"),
                        "USD",
                        new DateOnly(2025, 1, 1),
                        "medical",
                        0.21m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("69b6fc4f-8d7b-a81f-2a49-41998a4b1b10"),
                        "USD",
                        new DateOnly(2024, 1, 1),
                        "business",
                        0.67m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("717dd18a-a601-fcdf-e678-8c09616395fa"),
                        "USD",
                        new DateOnly(2022, 7, 1),
                        "moving",
                        0.22m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("73bb53d7-9a6f-6228-9896-368e21ef767c"),
                        "USD",
                        new DateOnly(2020, 1, 1),
                        "business",
                        0.575m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("7bc8041e-a2f1-48f9-7ac2-9b5d33966959"),
                        "USD",
                        new DateOnly(2020, 1, 1),
                        "moving",
                        0.17m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("7c4c5da8-f433-33e8-c0e3-5f7a67006ac8"),
                        "USD",
                        new DateOnly(2026, 1, 1),
                        "charity",
                        0.14m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("805557c0-7a85-bc3a-0743-1d5ec4b69656"),
                        "USD",
                        new DateOnly(2026, 1, 1),
                        "moving",
                        0.205m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("92c04825-dce4-e370-5138-7df8e5d81f85"),
                        "USD",
                        new DateOnly(2022, 1, 1),
                        "charity",
                        0.14m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("978fe4e5-74a0-c81b-68ee-aec68308d811"),
                        "USD",
                        new DateOnly(2022, 1, 1),
                        "moving",
                        0.18m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("98e8b6eb-bdab-b696-e9d7-fe37fa9b351e"),
                        "USD",
                        new DateOnly(2025, 1, 1),
                        "business",
                        0.70m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("a0b8c5d3-a8f4-82c1-c866-4a4324fc6036"),
                        "USD",
                        new DateOnly(2021, 1, 1),
                        "business",
                        0.56m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("a1bf252f-ab6c-ce5a-eca6-98b6db4b6dee"),
                        "USD",
                        new DateOnly(2024, 1, 1),
                        "charity",
                        0.14m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("ac928479-d380-4282-1f3e-f77b66e7eab7"),
                        "USD",
                        new DateOnly(2024, 1, 1),
                        "moving",
                        0.21m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("ad7bf7e5-a631-1a7a-ae6c-3fb5d161369a"),
                        "USD",
                        new DateOnly(2022, 1, 1),
                        "business",
                        0.585m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("b94a1b28-e6e6-2daf-954d-28d87253912b"),
                        "USD",
                        new DateOnly(2020, 1, 1),
                        "medical",
                        0.17m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("c55c6c5f-8533-017a-cbc9-53d309ffdca9"),
                        "USD",
                        new DateOnly(2021, 1, 1),
                        "moving",
                        0.16m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("d73dbd30-35c8-39c9-eb9e-6b59ad0ab7b5"),
                        "USD",
                        new DateOnly(2026, 1, 1),
                        "medical",
                        0.205m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("dd25efbe-dc19-f3f1-28bd-8fce8f92f2e3"),
                        "USD",
                        new DateOnly(2022, 7, 1),
                        "charity",
                        0.14m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("de4f7b45-234e-ffe1-04b1-cc7f8e3e9e49"),
                        "USD",
                        new DateOnly(2022, 7, 1),
                        "medical",
                        0.22m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("de584ec6-db4a-d2ce-49a1-afbd5252a9bf"),
                        "USD",
                        new DateOnly(2022, 7, 1),
                        "business",
                        0.625m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("df36630c-24d8-df03-7fa4-25a9b218c996"),
                        "USD",
                        new DateOnly(2026, 1, 1),
                        "business",
                        0.725m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("dfdcb798-446c-213a-bb8b-31716fcdc5dd"),
                        "USD",
                        new DateOnly(2023, 1, 1),
                        "moving",
                        0.22m,
                        "IRS standard mileage rates",
                    },
                    {
                        new Guid("ec561439-0614-b309-8148-3c48d474b623"),
                        "USD",
                        new DateOnly(2021, 1, 1),
                        "charity",
                        0.14m,
                        "IRS standard mileage rates",
                    },
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAssignmentProjections_TenantId_CustomerId",
                table: "CustomerAssignmentProjections",
                columns: new[] { "TenantId", "CustomerId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAssignmentProjections_TenantId_CustomerId_UserId",
                table: "CustomerAssignmentProjections",
                columns: new[] { "TenantId", "CustomerId", "UserId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_MobileStaffAccessLogs_TenantId_CustomerId_OccurredAtUtc",
                table: "MobileStaffAccessLogs",
                columns: new[] { "TenantId", "CustomerId", "OccurredAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_MobileStandardMileageRates_Purpose_EffectiveFrom",
                table: "MobileStandardMileageRates",
                columns: new[] { "Purpose", "EffectiveFrom" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CustomerAssignmentProjections");

            migrationBuilder.DropTable(name: "MobileStaffAccessLogs");

            migrationBuilder.DropTable(name: "MobileStandardMileageRates");
        }
    }
}
