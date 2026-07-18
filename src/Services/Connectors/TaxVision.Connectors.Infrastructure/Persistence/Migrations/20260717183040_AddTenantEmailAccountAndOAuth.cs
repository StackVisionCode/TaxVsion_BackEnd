using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Connectors.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantEmailAccountAndOAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OAuthConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AuthorizedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OAuthConnections", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "TenantEmailAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmailAddress = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    ProviderCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ConnectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastActivityAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantEmailAccounts", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "OAuthTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccessTokenCiphertext = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    AccessTokenNonce = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    AccessTokenTag = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    AccessTokenKeyVersion = table.Column<short>(type: "smallint", nullable: false),
                    RefreshTokenCiphertext = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    RefreshTokenNonce = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    RefreshTokenTag = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    RefreshTokenKeyVersion = table.Column<short>(type: "smallint", nullable: false),
                    AccessTokenExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RefreshedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OAuthTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OAuthTokens_OAuthConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "OAuthConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_OAuthConnections_AccountId",
                table: "OAuthConnections",
                column: "AccountId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_OAuthTokens_ConnectionId",
                table: "OAuthTokens",
                column: "ConnectionId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantEmailAccounts_TenantId_EmailAddress",
                table: "TenantEmailAccounts",
                columns: new[] { "TenantId", "EmailAddress" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "OAuthTokens");

            migrationBuilder.DropTable(name: "TenantEmailAccounts");

            migrationBuilder.DropTable(name: "OAuthConnections");
        }
    }
}
