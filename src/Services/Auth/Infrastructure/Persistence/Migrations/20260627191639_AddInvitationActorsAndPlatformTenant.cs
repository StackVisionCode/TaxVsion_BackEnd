using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvitationActorsAndPlatformTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActorType",
                table: "Users",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "TenantEmployee");

            migrationBuilder.AddColumn<Guid>(
                name: "CustomerId",
                table: "Users",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Tenants",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Customer");

            migrationBuilder.CreateTable(
                name: "Invitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    ActorType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InvitedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AcceptedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invitations", x => x.Id);
                });

            migrationBuilder.Sql(
                """
                UPDATE [Users]
                SET [ActorType] =
                    CASE
                        WHEN [Roles] LIKE '%PlatformAdmin%' THEN 'PlatformAdmin'
                        WHEN [Roles] LIKE '%TenantAdmin%' THEN 'TenantAdmin'
                        WHEN [Roles] LIKE '%CustomerPortal%' THEN 'CustomerPortal'
                        ELSE 'TenantEmployee'
                    END,
                    [Roles] =
                    CASE
                        WHEN [Roles] LIKE '%PlatformAdmin%' THEN N'["PlatformAdmin"]'
                        WHEN [Roles] LIKE '%TenantAdmin%' THEN N'["TenantAdmin"]'
                        WHEN [Roles] LIKE '%CustomerPortal%' THEN N'["CustomerPortal"]'
                        ELSE N'["TenantEmployee"]'
                    END;
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO [Invitations]
                (
                    [Id],
                    [Email],
                    [ActorType],
                    [CustomerId],
                    [InvitedByUserId],
                    [TokenHash],
                    [Status],
                    [CreatedAtUtc],
                    [ExpiresAtUtc],
                    [AcceptedAtUtc],
                    [AcceptedByUserId],
                    [CancelledAtUtc],
                    [CancelledByUserId],
                    [TenantId]
                )
                SELECT
                    NEWID(),
                    LOWER([AdminEmail]),
                    'TenantAdmin',
                    NULL,
                    NULL,
                    [AdminInvitationTokenHash],
                    CASE
                        WHEN [AdminUserId] IS NOT NULL THEN 'Accepted'
                        ELSE 'Pending'
                    END,
                    [CreatedAtUtc],
                    CASE
                        WHEN [AdminUserId] IS NOT NULL
                            THEN DATEADD(DAY, 7, [CreatedAtUtc])
                        ELSE DATEADD(DAY, 7, SYSUTCDATETIME())
                    END,
                    [AdminInvitationConsumedAtUtc],
                    [AdminUserId],
                    NULL,
                    NULL,
                    [Id]
                FROM [Tenants]
                WHERE [AdminEmail] IS NOT NULL
                  AND [AdminInvitationTokenHash] IS NOT NULL;
                """);

            migrationBuilder.DropIndex(
                name: "IX_Tenants_AdminEmail",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "AdminEmail",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "AdminInvitationConsumedAtUtc",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "AdminInvitationTokenHash",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "AdminUserId",
                table: "Tenants");

            migrationBuilder.InsertData(
                table: "Tenants",
                columns: new[] { "Id", "CreatedAtUtc", "DefaultTimeZoneId", "IsActive", "Kind", "Name", "SubDomain" },
                values: new object[] { new Guid("8f58a521-4c25-4d91-9f4e-7ad5df14c001"), new DateTime(2026, 6, 27, 0, 0, 0, 0, DateTimeKind.Utc), "Etc/UTC", true, "Platform", "TaxVision Platform", "platform-internal" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId_ActorType",
                table: "Users",
                columns: new[] { "TenantId", "ActorType" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId_CustomerId",
                table: "Users",
                columns: new[] { "TenantId", "CustomerId" },
                filter: "[CustomerId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_ExpiresAtUtc",
                table: "Invitations",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_TenantId_Email_Status",
                table: "Invitations",
                columns: new[] { "TenantId", "Email", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_TokenHash",
                table: "Invitations",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_TenantId_ActorType",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_TenantId_CustomerId",
                table: "Users");

            migrationBuilder.DeleteData(
                table: "Tenants",
                keyColumn: "Id",
                keyValue: new Guid("8f58a521-4c25-4d91-9f4e-7ad5df14c001"));

            migrationBuilder.DropColumn(
                name: "ActorType",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Tenants");

            migrationBuilder.AddColumn<string>(
                name: "AdminEmail",
                table: "Tenants",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AdminInvitationConsumedAtUtc",
                table: "Tenants",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdminInvitationTokenHash",
                table: "Tenants",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AdminUserId",
                table: "Tenants",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE tenant
                SET
                    tenant.[AdminEmail] = invitation.[Email],
                    tenant.[AdminInvitationTokenHash] = invitation.[TokenHash],
                    tenant.[AdminInvitationConsumedAtUtc] = invitation.[AcceptedAtUtc],
                    tenant.[AdminUserId] = invitation.[AcceptedByUserId]
                FROM [Tenants] tenant
                INNER JOIN [Invitations] invitation
                    ON invitation.[TenantId] = tenant.[Id]
                WHERE invitation.[ActorType] = 'TenantAdmin'
                  AND invitation.[InvitedByUserId] IS NULL;
                """);

            migrationBuilder.DropTable(
                name: "Invitations");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_AdminEmail",
                table: "Tenants",
                column: "AdminEmail");
        }
    }
}
