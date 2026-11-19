using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Mobile.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTripTrackingAndMileageRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Vehicles_TenantId_CustomerId_Id",
                table: "Vehicles",
                columns: new[] { "TenantId", "CustomerId", "Id" }
            );

            migrationBuilder.CreateTable(
                name: "MobileMileageRates",
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
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobileMileageRates", x => x.Id);
                    table.CheckConstraint("CK_MobileMileageRates_Rate", "[RatePerMile] >= 0 AND [RatePerMile] <= 1000");
                }
            );

            migrationBuilder.CreateTable(
                name: "MobileTrips",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientTripId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VehiclePlate = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastObservedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    LastPointSequence = table.Column<long>(type: "bigint", nullable: false),
                    LastPointAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastLatitude = table.Column<double>(type: "float", nullable: false),
                    LastLongitude = table.Column<double>(type: "float", nullable: false),
                    HasDistanceAnchor = table.Column<bool>(type: "bit", nullable: false),
                    DistanceMeters = table.Column<double>(type: "float", nullable: false),
                    ActiveDurationSeconds = table.Column<double>(type: "float", nullable: false),
                    MaxSpeedMetersPerSecond = table.Column<double>(type: "float", nullable: false),
                    MileageRateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RatePerMile = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    EstimatedAmount = table.Column<decimal>(
                        type: "decimal(19,2)",
                        precision: 19,
                        scale: 2,
                        nullable: true
                    ),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobileTrips", x => x.Id);
                    table.UniqueConstraint(
                        "AK_MobileTrips_TenantId_CustomerId_Id",
                        x => new
                        {
                            x.TenantId,
                            x.CustomerId,
                            x.Id,
                        }
                    );
                    table.CheckConstraint(
                        "CK_MobileTrips_Distance",
                        "[DistanceMeters] >= 0 AND [ActiveDurationSeconds] >= 0"
                    );
                    table.CheckConstraint("CK_MobileTrips_Revision", "[Revision] >= 1 AND [LastPointSequence] >= 0");
                    table.ForeignKey(
                        name: "FK_MobileTrips_MobileMileageRates_MileageRateId",
                        column: x => x.MileageRateId,
                        principalTable: "MobileMileageRates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_MobileTrips_Vehicles_TenantId_CustomerId_VehicleId",
                        columns: x => new
                        {
                            x.TenantId,
                            x.CustomerId,
                            x.VehicleId,
                        },
                        principalTable: "Vehicles",
                        principalColumns: new[] { "TenantId", "CustomerId", "Id" },
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "MobileTripPoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TripId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    ObservedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Latitude = table.Column<double>(type: "float", nullable: false),
                    Longitude = table.Column<double>(type: "float", nullable: false),
                    AccuracyMeters = table.Column<double>(type: "float", nullable: false),
                    SpeedMetersPerSecond = table.Column<double>(type: "float", nullable: true),
                    AltitudeMeters = table.Column<double>(type: "float", nullable: true),
                    Heading = table.Column<double>(type: "float", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobileTripPoints", x => x.Id);
                    table.CheckConstraint(
                        "CK_MobileTripPoints_Location",
                        "[Latitude] >= -90 AND [Latitude] <= 90 AND [Longitude] >= -180 AND [Longitude] <= 180 AND [AccuracyMeters] >= 0 AND [AccuracyMeters] <= 10000"
                    );
                    table.CheckConstraint("CK_MobileTripPoints_Sequence", "[Sequence] >= 0");
                    table.ForeignKey(
                        name: "FK_MobileTripPoints_MobileTrips_TenantId_CustomerId_TripId",
                        columns: x => new
                        {
                            x.TenantId,
                            x.CustomerId,
                            x.TripId,
                        },
                        principalTable: "MobileTrips",
                        principalColumns: new[] { "TenantId", "CustomerId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "MobileTripStateChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TripId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ObservedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobileTripStateChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MobileTripStateChanges_MobileTrips_TenantId_CustomerId_TripId",
                        columns: x => new
                        {
                            x.TenantId,
                            x.CustomerId,
                            x.TripId,
                        },
                        principalTable: "MobileTrips",
                        principalColumns: new[] { "TenantId", "CustomerId", "Id" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_MobileMileageRates_TenantId_Purpose_EffectiveFrom",
                table: "MobileMileageRates",
                columns: new[] { "TenantId", "Purpose", "EffectiveFrom" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_MobileTripPoints_TenantId_CustomerId_TripId_Sequence",
                table: "MobileTripPoints",
                columns: new[] { "TenantId", "CustomerId", "TripId", "Sequence" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_MobileTrips_MileageRateId",
                table: "MobileTrips",
                column: "MileageRateId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_MobileTrips_TenantId_CustomerId_ClientTripId",
                table: "MobileTrips",
                columns: new[] { "TenantId", "CustomerId", "ClientTripId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_MobileTrips_TenantId_CustomerId_StartedAtUtc",
                table: "MobileTrips",
                columns: new[] { "TenantId", "CustomerId", "StartedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_MobileTrips_TenantId_CustomerId_Status",
                table: "MobileTrips",
                columns: new[] { "TenantId", "CustomerId", "Status" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_MobileTrips_TenantId_CustomerId_VehicleId",
                table: "MobileTrips",
                columns: new[] { "TenantId", "CustomerId", "VehicleId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_MobileTripStateChanges_TenantId_CustomerId_TripId_Sequence",
                table: "MobileTripStateChanges",
                columns: new[] { "TenantId", "CustomerId", "TripId", "Sequence" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "MobileTripPoints");

            migrationBuilder.DropTable(name: "MobileTripStateChanges");

            migrationBuilder.DropTable(name: "MobileTrips");

            migrationBuilder.DropTable(name: "MobileMileageRates");

            migrationBuilder.DropUniqueConstraint(name: "AK_Vehicles_TenantId_CustomerId_Id", table: "Vehicles");
        }
    }
}
