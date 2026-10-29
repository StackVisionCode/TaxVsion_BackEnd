using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormVersionRuleSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActionSetJson",
                table: "FormTemplateVersions",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]"
            );

            migrationBuilder.AddColumn<string>(
                name: "MappingSetJson",
                table: "FormTemplateVersions",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]"
            );

            migrationBuilder.AddColumn<int>(
                name: "TargetCatalogVersion",
                table: "FormTemplateVersions",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            // Las versiones ya publicadas toman las reglas de su propia definición canónica.
            migrationBuilder.Sql(
                """
                UPDATE [FormTemplateVersions]
                SET [MappingSetJson] = COALESCE(JSON_QUERY([DefinitionJson], '$.mappings'), N'[]'),
                    [ActionSetJson] = COALESCE(JSON_QUERY([DefinitionJson], '$.actions'), N'[]');
                """
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_FormTemplateVersions_ActionSet_IsJson",
                table: "FormTemplateVersions",
                sql: "ISJSON([ActionSetJson]) = 1"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_FormTemplateVersions_MappingSet_IsJson",
                table: "FormTemplateVersions",
                sql: "ISJSON([MappingSetJson]) = 1"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_FormTemplateVersions_ActionSet_IsJson",
                table: "FormTemplateVersions"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_FormTemplateVersions_MappingSet_IsJson",
                table: "FormTemplateVersions"
            );

            migrationBuilder.DropColumn(name: "ActionSetJson", table: "FormTemplateVersions");

            migrationBuilder.DropColumn(name: "MappingSetJson", table: "FormTemplateVersions");

            migrationBuilder.DropColumn(name: "TargetCatalogVersion", table: "FormTemplateVersions");
        }
    }
}
