using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Customer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomerImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerImportAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Strategy = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SourceKind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    SourceFileName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    TotalRows = table.Column<int>(type: "int", nullable: false),
                    ProcessedRows = table.Column<int>(type: "int", nullable: false),
                    SuccessCount = table.Column<int>(type: "int", nullable: false),
                    UpdatedCount = table.Column<int>(type: "int", nullable: false),
                    SkippedCount = table.Column<int>(type: "int", nullable: false),
                    FailedCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CanceledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CanceledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerImportAttempts", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "CustomerImportFiles",
                columns: table => new
                {
                    ImportAttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerImportFiles", x => x.ImportAttemptId);
                }
            );

            migrationBuilder.CreateTable(
                name: "CustomerImportRows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerImportAttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    ResultingCustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MatchedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerImportRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerImportRows_CustomerImportAttempts_CustomerImportAttemptId",
                        column: x => x.CustomerImportAttemptId,
                        principalTable: "CustomerImportAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CustomerImportAttempts_Created",
                table: "CustomerImportAttempts",
                column: "CreatedAtUtc"
            );

            migrationBuilder.CreateIndex(
                name: "IX_CustomerImportAttempts_Tenant_IdempotencyKey",
                table: "CustomerImportAttempts",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_CustomerImportAttempts_Tenant_Status_Created",
                table: "CustomerImportAttempts",
                columns: new[] { "TenantId", "Status", "CreatedAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CustomerImportRows_Attempt_Row",
                table: "CustomerImportRows",
                columns: new[] { "CustomerImportAttemptId", "RowNumber" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CustomerImportRows_Attempt_Status",
                table: "CustomerImportRows",
                columns: new[] { "CustomerImportAttemptId", "Status" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CustomerImportFiles");

            migrationBuilder.DropTable(name: "CustomerImportRows");

            migrationBuilder.DropTable(name: "CustomerImportAttempts");
        }
    }
}
