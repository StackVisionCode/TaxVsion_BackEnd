using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Sube los multipliers de lectura (F) y escritura (G) para darle más aire a las oficinas:
    /// starter 1.0 → 2.0, pro 3.0 → 5.0. Enterprise (10.0) no cambia. El resto de categorías
    /// (H/I/J/K/L/M/N/O) quedan igual. Data-only sobre PlanRateLimits; propaga por el TTL de 5 min
    /// del catálogo global (sin evento, sin re-proyección).
    /// </summary>
    public partial class AdjustPlanRateLimitMultipliersReadWrite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // F (lectura) y G (escritura) — starter 1.0 → 2.0
            migrationBuilder.UpdateData(
                table: "PlanRateLimits",
                keyColumn: "Id",
                keyValue: new Guid("c3000000-0000-0000-0000-000000000000"), // F starter
                column: "MultiplierOverride",
                value: 2.0m
            );
            migrationBuilder.UpdateData(
                table: "PlanRateLimits",
                keyColumn: "Id",
                keyValue: new Guid("c3000000-0000-0000-0000-000000000001"), // G starter
                column: "MultiplierOverride",
                value: 2.0m
            );

            // F (lectura) y G (escritura) — pro 3.0 → 5.0
            migrationBuilder.UpdateData(
                table: "PlanRateLimits",
                keyColumn: "Id",
                keyValue: new Guid("c3000000-0000-0000-0000-000000000010"), // F pro
                column: "MultiplierOverride",
                value: 5.0m
            );
            migrationBuilder.UpdateData(
                table: "PlanRateLimits",
                keyColumn: "Id",
                keyValue: new Guid("c3000000-0000-0000-0000-000000000011"), // G pro
                column: "MultiplierOverride",
                value: 5.0m
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "PlanRateLimits",
                keyColumn: "Id",
                keyValue: new Guid("c3000000-0000-0000-0000-000000000000"), // F starter
                column: "MultiplierOverride",
                value: 1.0m
            );
            migrationBuilder.UpdateData(
                table: "PlanRateLimits",
                keyColumn: "Id",
                keyValue: new Guid("c3000000-0000-0000-0000-000000000001"), // G starter
                column: "MultiplierOverride",
                value: 1.0m
            );
            migrationBuilder.UpdateData(
                table: "PlanRateLimits",
                keyColumn: "Id",
                keyValue: new Guid("c3000000-0000-0000-0000-000000000010"), // F pro
                column: "MultiplierOverride",
                value: 3.0m
            );
            migrationBuilder.UpdateData(
                table: "PlanRateLimits",
                keyColumn: "Id",
                keyValue: new Guid("c3000000-0000-0000-0000-000000000011"), // G pro
                column: "MultiplierOverride",
                value: 3.0m
            );
        }
    }
}
