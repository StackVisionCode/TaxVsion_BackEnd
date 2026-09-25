using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Búsquedas/listados (H) escalan igual que lectura/escritura: starter 1.0 → 2.0, pro 3.0 → 5.0,
    /// enterprise 15.0 → 10.0 (su base sube de 20 a 60/min, así que igual queda muy por encima).
    /// Las filas F/G de starter y pro ya valían 2.0/5.0 en la base (AdjustPlanRateLimitMultipliersReadWrite
    /// fue data-only y dejó el HasData atrás); se re-afirman acá para que el seed deje de tener drift, y
    /// por eso el Down solo revierte H. Data-only; propaga por el TTL de 5 min del catálogo global.
    /// </summary>
    public partial class AdjustPlanRateLimitMultipliersSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // F y G — ya eran estos valores en la base; solo alinean el seed.
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

            // H — el cambio real.
            migrationBuilder.UpdateData(
                table: "PlanRateLimits",
                keyColumn: "Id",
                keyValue: new Guid("c3000000-0000-0000-0000-000000000002"), // H starter
                column: "MultiplierOverride",
                value: 2.0m
            );
            migrationBuilder.UpdateData(
                table: "PlanRateLimits",
                keyColumn: "Id",
                keyValue: new Guid("c3000000-0000-0000-0000-000000000012"), // H pro
                column: "MultiplierOverride",
                value: 5.0m
            );
            migrationBuilder.UpdateData(
                table: "PlanRateLimits",
                keyColumn: "Id",
                keyValue: new Guid("c3000000-0000-0000-0000-000000000022"), // H enterprise
                column: "MultiplierOverride",
                value: 10.0m
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "PlanRateLimits",
                keyColumn: "Id",
                keyValue: new Guid("c3000000-0000-0000-0000-000000000002"), // H starter
                column: "MultiplierOverride",
                value: 1.0m
            );
            migrationBuilder.UpdateData(
                table: "PlanRateLimits",
                keyColumn: "Id",
                keyValue: new Guid("c3000000-0000-0000-0000-000000000012"), // H pro
                column: "MultiplierOverride",
                value: 3.0m
            );
            migrationBuilder.UpdateData(
                table: "PlanRateLimits",
                keyColumn: "Id",
                keyValue: new Guid("c3000000-0000-0000-0000-000000000022"), // H enterprise
                column: "MultiplierOverride",
                value: 15.0m
            );
        }
    }
}
