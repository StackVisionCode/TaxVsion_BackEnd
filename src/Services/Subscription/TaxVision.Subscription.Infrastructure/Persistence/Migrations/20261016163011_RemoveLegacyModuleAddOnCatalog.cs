using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLegacyModuleAddOnCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM AddOnDefinitions WHERE Code = 'addon-email')
                   AND NOT EXISTS (SELECT 1 FROM AddOnDefinitions WHERE Code = 'module-addon-email')
                    UPDATE AddOnDefinitions SET Code = 'module-addon-email', Name = 'Email' WHERE Code = 'addon-email';

                IF EXISTS (SELECT 1 FROM AddOnDefinitions WHERE Code = 'addon-meetings')
                   AND NOT EXISTS (SELECT 1 FROM AddOnDefinitions WHERE Code = 'module-addon-meetings')
                    UPDATE AddOnDefinitions SET Code = 'module-addon-meetings', Name = 'Meetings' WHERE Code = 'addon-meetings';

                IF EXISTS (SELECT 1 FROM AddOnDefinitions WHERE Code = 'addon-campaigns')
                   AND NOT EXISTS (SELECT 1 FROM AddOnDefinitions WHERE Code = 'module-addon-campaigns')
                    UPDATE AddOnDefinitions SET Code = 'module-addon-campaigns', Name = 'Campaigns' WHERE Code = 'addon-campaigns';

                DECLARE @LegacyModuleAddOns TABLE (Id uniqueidentifier PRIMARY KEY);
                INSERT INTO @LegacyModuleAddOns (Id)
                SELECT Id
                FROM AddOnDefinitions
                WHERE Category = 'module'
                  AND Code NOT LIKE 'module-addon-%';

                DELETE FROM AddOnPurchaseIntents WHERE AddOnDefinitionId IN (SELECT Id FROM @LegacyModuleAddOns);
                DELETE FROM TenantAddOns WHERE AddOnDefinitionId IN (SELECT Id FROM @LegacyModuleAddOns);
                DELETE FROM AddOnFeatures WHERE AddOnDefinitionId IN (SELECT Id FROM @LegacyModuleAddOns);
                DELETE FROM AddOnEntitlementDefinitions WHERE AddOnDefinitionId IN (SELECT Id FROM @LegacyModuleAddOns);
                DELETE FROM AddOnPriceTiers WHERE AddOnDefinitionId IN (SELECT Id FROM @LegacyModuleAddOns);
                DELETE FROM AddOnDefinitions WHERE Id IN (SELECT Id FROM @LegacyModuleAddOns);
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deleted purchase history cannot be reconstructed safely.
        }
    }
}
