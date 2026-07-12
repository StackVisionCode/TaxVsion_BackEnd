using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSecurityRbacMfaSessionsAndPlanLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "Users",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified)
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "DeactivatedAtUtc",
                table: "Users",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<bool>(
                name: "EmailVerified",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<int>(
                name: "FailedLoginCount",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "LockoutEndUtc",
                table: "Users",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<bool>(
                name: "MfaEnabled",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordChangedAtUtc",
                table: "Users",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "PermissionsVersion",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "Users",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true
            );

            migrationBuilder.AddColumn<bool>(
                name: "PhoneVerified",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Users",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacedByTokenId",
                table: "RefreshTokens",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "RevokedReason",
                table: "RefreshTokens",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "SessionId",
                table: "RefreshTokens",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "RefreshTokens",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000")
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSentAtUtc",
                table: "Invitations",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "ResendCount",
                table: "Invitations",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<string>(
                name: "RoleIdsJson",
                table: "Invitations",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "AuthAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TargetType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DetailsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthAuditLogs", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "EmailVerificationTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NewEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailVerificationTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailVerificationTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "MfaChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MfaMethodId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LoginTicketHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OtpHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfaChallenges", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "MfaMethods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SecretCiphertext = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Destination = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    IsConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    IsPreferred = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MfaMethods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MfaMethods_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "PasswordResetTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequestedIp = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordResetTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PasswordResetTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "Permissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Module = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    IsCustomerPortal = table.Column<bool>(type: "bit", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "PhoneVerificationTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhoneVerificationTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhoneVerificationTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "RecoveryCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecoveryCodes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "TenantMfaPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequireForAdmins = table.Column<bool>(type: "bit", nullable: false),
                    RequireForEmployees = table.Column<bool>(type: "bit", nullable: false),
                    RequireForCustomerPortal = table.Column<bool>(type: "bit", nullable: false),
                    TrustedDeviceDays = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantMfaPolicies", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "TenantPlanLimits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    MaxUsers = table.Column<int>(type: "int", nullable: false),
                    MaxPendingInvitations = table.Column<int>(type: "int", nullable: false),
                    StorageQuotaBytes = table.Column<long>(type: "bigint", nullable: false),
                    EnabledModulesJson = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    IsSuspendedForBilling = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantPlanLimits", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "TrustedDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceTokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserAgent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustedDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrustedDevices_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "UserSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedReason = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => new { x.RoleId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_RolePermissions_Permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "Permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_RolePermissions_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "Code", "Description", "IsCustomerPortal", "Module" },
                values: new object[,]
                {
                    {
                        new Guid("a1000000-0000-0000-0000-000000000001"),
                        "users.view",
                        "Ver usuarios del tenant",
                        false,
                        "users",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000002"),
                        "users.invite",
                        "Invitar usuarios",
                        false,
                        "users",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000003"),
                        "users.manage",
                        "Activar, desactivar y editar usuarios",
                        false,
                        "users",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000004"),
                        "roles.manage",
                        "Gestionar roles y permisos",
                        false,
                        "users",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000005"),
                        "audit.view",
                        "Consultar auditoría",
                        false,
                        "audit",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000006"),
                        "settings.manage",
                        "Gestionar configuración del tenant",
                        false,
                        "settings",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000007"),
                        "billing.view",
                        "Ver facturación y suscripción",
                        false,
                        "billing",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000008"),
                        "billing.manage",
                        "Gestionar métodos de pago y facturación",
                        false,
                        "billing",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000009"),
                        "subscription.manage",
                        "Cambiar plan y gestionar suscripción",
                        false,
                        "billing",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000010"),
                        "customers.view",
                        "Ver clientes",
                        false,
                        "customers",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000011"),
                        "customers.manage",
                        "Crear y editar clientes",
                        false,
                        "customers",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000012"),
                        "signatures.request",
                        "Solicitar firmas",
                        false,
                        "signatures",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000013"),
                        "documents.view",
                        "Ver documentos",
                        false,
                        "documents",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000014"),
                        "documents.manage",
                        "Gestionar documentos",
                        false,
                        "documents",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000015"),
                        "email.use",
                        "Usar el módulo de correo",
                        false,
                        "email",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000016"),
                        "comms.calls",
                        "Realizar llamadas y meetings",
                        false,
                        "comms",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000017"),
                        "campaigns.manage",
                        "Gestionar campañas",
                        false,
                        "campaigns",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000018"),
                        "reports.view",
                        "Ver dashboard y reportes",
                        false,
                        "reports",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000019"),
                        "portal.calls.use",
                        "El cliente puede realizar llamadas",
                        true,
                        "portal",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000020"),
                        "portal.miles.use",
                        "El cliente puede usar el módulo de millas",
                        true,
                        "portal",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000021"),
                        "portal.folders.view",
                        "El cliente puede ver folders de su perfil",
                        true,
                        "portal",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000022"),
                        "portal.signatures.sign",
                        "El cliente puede firmar documentos",
                        true,
                        "portal",
                    },
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_SessionId",
                table: "RefreshTokens",
                column: "SessionId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AuthAuditLogs_TenantId_Action",
                table: "AuthAuditLogs",
                columns: new[] { "TenantId", "Action" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_AuthAuditLogs_TenantId_OccurredAtUtc",
                table: "AuthAuditLogs",
                columns: new[] { "TenantId", "OccurredAtUtc" },
                descending: new[] { false, true }
            );

            migrationBuilder.CreateIndex(
                name: "IX_AuthAuditLogs_TenantId_UserId_OccurredAtUtc",
                table: "AuthAuditLogs",
                columns: new[] { "TenantId", "UserId", "OccurredAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_EmailVerificationTokens_TokenHash",
                table: "EmailVerificationTokens",
                column: "TokenHash",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_EmailVerificationTokens_UserId",
                table: "EmailVerificationTokens",
                column: "UserId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_MfaChallenges_ExpiresAtUtc",
                table: "MfaChallenges",
                column: "ExpiresAtUtc"
            );

            migrationBuilder.CreateIndex(
                name: "IX_MfaChallenges_LoginTicketHash",
                table: "MfaChallenges",
                column: "LoginTicketHash",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_MfaMethods_UserId_Type",
                table: "MfaMethods",
                columns: new[] { "UserId", "Type" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_TokenHash",
                table: "PasswordResetTokens",
                column: "TokenHash",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_UserId_UsedAtUtc",
                table: "PasswordResetTokens",
                columns: new[] { "UserId", "UsedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Permissions_Code",
                table: "Permissions",
                column: "Code",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_PhoneVerificationTokens_UserId_UsedAtUtc",
                table: "PhoneVerificationTokens",
                columns: new[] { "UserId", "UsedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryCodes_UserId_UsedAtUtc",
                table: "RecoveryCodes",
                columns: new[] { "UserId", "UsedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_PermissionId",
                table: "RolePermissions",
                column: "PermissionId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Roles_TenantId_Name",
                table: "Roles",
                columns: new[] { "TenantId", "Name" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_TrustedDevices_DeviceTokenHash",
                table: "TrustedDevices",
                column: "DeviceTokenHash",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_TrustedDevices_UserId_RevokedAtUtc",
                table: "TrustedDevices",
                columns: new[] { "UserId", "RevokedAtUtc" }
            );

            migrationBuilder.CreateIndex(name: "IX_UserRoles_RoleId", table: "UserRoles", column: "RoleId");

            migrationBuilder.CreateIndex(name: "IX_UserSessions_TenantId", table: "UserSessions", column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_UserId_RevokedAtUtc",
                table: "UserSessions",
                columns: new[] { "UserId", "RevokedAtUtc" }
            );

            migrationBuilder.AddForeignKey(
                name: "FK_RefreshTokens_UserSessions_SessionId",
                table: "RefreshTokens",
                column: "SessionId",
                principalTable: "UserSessions",
                principalColumn: "Id"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_RefreshTokens_UserSessions_SessionId", table: "RefreshTokens");

            migrationBuilder.DropTable(name: "AuthAuditLogs");

            migrationBuilder.DropTable(name: "EmailVerificationTokens");

            migrationBuilder.DropTable(name: "MfaChallenges");

            migrationBuilder.DropTable(name: "MfaMethods");

            migrationBuilder.DropTable(name: "PasswordResetTokens");

            migrationBuilder.DropTable(name: "PhoneVerificationTokens");

            migrationBuilder.DropTable(name: "RecoveryCodes");

            migrationBuilder.DropTable(name: "RolePermissions");

            migrationBuilder.DropTable(name: "TenantMfaPolicies");

            migrationBuilder.DropTable(name: "TenantPlanLimits");

            migrationBuilder.DropTable(name: "TrustedDevices");

            migrationBuilder.DropTable(name: "UserRoles");

            migrationBuilder.DropTable(name: "UserSessions");

            migrationBuilder.DropTable(name: "Permissions");

            migrationBuilder.DropTable(name: "Roles");

            migrationBuilder.DropIndex(name: "IX_RefreshTokens_SessionId", table: "RefreshTokens");

            migrationBuilder.DropColumn(name: "CreatedAtUtc", table: "Users");

            migrationBuilder.DropColumn(name: "DeactivatedAtUtc", table: "Users");

            migrationBuilder.DropColumn(name: "EmailVerified", table: "Users");

            migrationBuilder.DropColumn(name: "FailedLoginCount", table: "Users");

            migrationBuilder.DropColumn(name: "LockoutEndUtc", table: "Users");

            migrationBuilder.DropColumn(name: "MfaEnabled", table: "Users");

            migrationBuilder.DropColumn(name: "PasswordChangedAtUtc", table: "Users");

            migrationBuilder.DropColumn(name: "PermissionsVersion", table: "Users");

            migrationBuilder.DropColumn(name: "PhoneNumber", table: "Users");

            migrationBuilder.DropColumn(name: "PhoneVerified", table: "Users");

            migrationBuilder.DropColumn(name: "TimeZoneId", table: "Users");

            migrationBuilder.DropColumn(name: "ReplacedByTokenId", table: "RefreshTokens");

            migrationBuilder.DropColumn(name: "RevokedReason", table: "RefreshTokens");

            migrationBuilder.DropColumn(name: "SessionId", table: "RefreshTokens");

            migrationBuilder.DropColumn(name: "TenantId", table: "RefreshTokens");

            migrationBuilder.DropColumn(name: "LastSentAtUtc", table: "Invitations");

            migrationBuilder.DropColumn(name: "ResendCount", table: "Invitations");

            migrationBuilder.DropColumn(name: "RoleIdsJson", table: "Invitations");
        }
    }
}
