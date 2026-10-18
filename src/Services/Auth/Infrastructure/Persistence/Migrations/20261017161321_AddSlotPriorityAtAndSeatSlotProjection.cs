using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSlotPriorityAtAndSeatSlotProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SlotPriorityAt",
                table: "Users",
                type: "datetime2",
                nullable: true
            );

            // G-D1: identificamos al owner por OnboardingId IS NOT NULL (más confiable que buscar por
            // rol en este punto de la migración; los roles han sido renombrados históricamente). Fallback
            // al TenantAdmin más antiguo si ningún admin del tenant tiene OnboardingId (CTE LegacyOwners).
            migrationBuilder.Sql(
                """
                UPDATE [Users]
                SET [SlotPriorityAt] = NULL
                WHERE [IsActive] = 0
                   OR [ActorType] IN (N'CustomerPortal', N'PlatformAdmin');

                UPDATE [Users]
                SET [SlotPriorityAt] = '0001-01-01T00:00:00.0000000'
                WHERE [IsActive] = 1
                  AND [ActorType] = N'TenantAdmin'
                  AND [OnboardingId] IS NOT NULL;

                ;WITH [LegacyOwners] AS
                (
                    SELECT
                        [u].[Id],
                        ROW_NUMBER() OVER (
                            PARTITION BY [u].[TenantId]
                            ORDER BY [u].[CreatedAtUtc], [u].[Id]
                        ) AS [rn]
                    FROM [Users] AS [u]
                    WHERE [u].[IsActive] = 1
                      AND [u].[ActorType] = N'TenantAdmin'
                      AND NOT EXISTS
                      (
                          SELECT 1
                          FROM [Users] AS [owner]
                          WHERE [owner].[TenantId] = [u].[TenantId]
                            AND [owner].[IsActive] = 1
                            AND [owner].[ActorType] = N'TenantAdmin'
                            AND [owner].[SlotPriorityAt] = '0001-01-01T00:00:00.0000000'
                      )
                )
                UPDATE [u]
                SET [SlotPriorityAt] = '0001-01-01T00:00:00.0000000'
                FROM [Users] AS [u]
                INNER JOIN [LegacyOwners] AS [owner] ON [owner].[Id] = [u].[Id]
                WHERE [owner].[rn] = 1;

                UPDATE [Users]
                SET [SlotPriorityAt] = COALESCE([CreatedAtUtc], SYSUTCDATETIME())
                WHERE [IsActive] = 1
                  AND [ActorType] IN (N'TenantEmployee', N'TenantAdmin')
                  AND [SlotPriorityAt] IS NULL;
                """
            );

            migrationBuilder.CreateTable(
                name: "TenantSeatSlotProjections",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SlotIndex = table.Column<int>(type: "int", nullable: false),
                    EffectiveCap = table.Column<int>(type: "int", nullable: false),
                    IsGated = table.Column<bool>(type: "bit", nullable: false),
                    ComputedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevisionNumber = table.Column<long>(type: "bigint", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSeatSlotProjections", x => new { x.TenantId, x.UserId });
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantSeatSlotProjections_TenantId_IsGated",
                table: "TenantSeatSlotProjections",
                columns: new[] { "TenantId", "IsGated" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantSeatSlotProjections_TenantId_SlotIndex",
                table: "TenantSeatSlotProjections",
                columns: new[] { "TenantId", "SlotIndex" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TenantSeatSlotProjections");

            migrationBuilder.DropColumn(name: "SlotPriorityAt", table: "Users");
        }
    }
}
