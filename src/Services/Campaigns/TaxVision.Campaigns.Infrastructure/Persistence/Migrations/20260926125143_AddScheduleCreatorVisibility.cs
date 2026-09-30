using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Campaigns.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Aditiva. El <c>defaultValue</c> de <c>CreatorCanViewAllCustomers</c> se cambió a mano de
    /// <c>false</c> (lo que generó el scaffolding, por el default del tipo <c>bool</c>) a <c>true</c>: es
    /// la visibilidad con la que los schedules YA agendados vienen disparando. Con <c>false</c>, cada
    /// campaña recurrente existente pasaría de golpe a enviarse a menos clientes — un cambio de a quién
    /// se le manda un correo, en silencio y sin que nadie lo haya pedido (§R.7 del plan).
    /// </summary>
    public partial class AddScheduleCreatorVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "CampaignSchedules",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<bool>(
                name: "CreatorCanViewAllCustomers",
                table: "CampaignSchedules",
                type: "bit",
                nullable: false,
                defaultValue: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CreatedByUserId", table: "CampaignSchedules");

            migrationBuilder.DropColumn(name: "CreatorCanViewAllCustomers", table: "CampaignSchedules");
        }
    }
}
