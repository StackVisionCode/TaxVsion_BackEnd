using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingTenantOnboardings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantOnboardings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    EmailVerifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PaymentStatus = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: true),
                    PaymentReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PaymentCompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RegistrationTokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RegistrationTokenExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RegistrationTokenUsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OfficeName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RequestedSubdomain = table.Column<string>(type: "nvarchar(63)", maxLength: 63, nullable: true),
                    TermsVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TermsContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TermsAcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AcceptedFromIp = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProvisioningStartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RegistrationCompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CurrentStep = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    FailedStep = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantOnboardings", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantOnboardings_Email_Status",
                table: "TenantOnboardings",
                columns: new[] { "Email", "Status" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantOnboardings_RegistrationTokenHash",
                table: "TenantOnboardings",
                column: "RegistrationTokenHash",
                unique: true,
                filter: "[RegistrationTokenHash] IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantOnboardings_Status_CreatedAtUtc",
                table: "TenantOnboardings",
                columns: new[] { "Status", "CreatedAtUtc" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TenantOnboardings");
        }
    }
}
