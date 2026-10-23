using BuildingBlocks.Messaging.ClientFormsIntegrationEvents.Targets;
using TaxVision.Tasks.ClientForms.Application.Templates.Queries;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class PublishFormTemplateMappingTests
{
    [Fact]
    public async Task Onboarding_mapped_to_create_customer_publishes_and_pins_its_rule_sets()
    {
        var template = FormMappingTestDefinitions.Template(FormMappingTestDefinitions.Onboarding);
        var versions = new MemoryFormTemplateVersionRepository();

        var result = await FormMappingTestDefinitions.PublishAsync(
            template,
            FormTemplateHandlerTestDoubles.Publisher,
            versions
        );

        Assert.True(result.Value.Published, string.Join(", ", result.Value.Issues.Select(issue => issue.Code)));
        var version = Assert.Single(versions.Items);
        Assert.Contains("\"targetKey\":\"customer.primary_email\"", version.MappingSetJson, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"createCustomer\"", version.ActionSetJson, StringComparison.Ordinal);
        Assert.Equal(CustomerDataCollectionTargets.CatalogVersion, version.TargetCatalogVersion);
        Assert.Equal(CustomerDataCollectionTargets.CatalogVersion, result.Value.Version!.TargetCatalogVersion);
    }

    [Fact]
    public async Task Publisher_without_target_permissions_gets_mapping_issues_and_nothing_is_published()
    {
        var template = FormMappingTestDefinitions.Template(FormMappingTestDefinitions.Onboarding);
        var versions = new MemoryFormTemplateVersionRepository();

        var result = await FormMappingTestDefinitions.PublishAsync(
            template,
            FormMappingTestDefinitions.NoPermissions,
            versions
        );

        Assert.False(result.Value.Published);
        Assert.All(result.Value.Issues, issue => Assert.Equal("mapping.permission_denied", issue.Code));
        Assert.Contains(result.Value.Issues, issue => issue.Path == "/actions/0/type");
        Assert.Empty(versions.Items);
    }

    [Fact]
    public async Task Missing_required_target_blocks_publish()
    {
        var definition = FormMappingTestDefinitions.Onboarding.Replace(
            """{"componentId":"fd_email0001","targetKey":"customer.primary_email","transform":"lowercase"},""",
            string.Empty,
            StringComparison.Ordinal
        );
        var template = FormMappingTestDefinitions.Template(definition);
        var versions = new MemoryFormTemplateVersionRepository();

        var result = await FormMappingTestDefinitions.PublishAsync(
            template,
            FormTemplateHandlerTestDoubles.Publisher,
            versions
        );

        Assert.False(result.Value.Published);
        var issue = Assert.Single(result.Value.Issues);
        Assert.Equal(("mapping.required_target_missing", "/actions/0"), (issue.Code, issue.Path));
        Assert.Empty(versions.Items);
    }

    [Fact]
    public async Task Fiscal_target_needs_a_tenant_admin_publisher()
    {
        var definition = FormMappingTestDefinitions
            .Onboarding.Replace(
                """{"id":"fd_addr00001","type":"address","label":"Home address","props":{"country":"US"}}""",
                """{"id":"fd_addr00001","type":"address","label":"Home address","props":{"country":"US"}},{"id":"fd_taxid001","type":"taxId","label":"Taxpayer identifier","props":{"kinds":["ssn"]}}""",
                StringComparison.Ordinal
            )
            .Replace(
                """{"componentId":"fd_addr00001","targetKey":"customer.address.home"}""",
                """{"componentId":"fd_addr00001","targetKey":"customer.address.home"},{"componentId":"fd_taxid001","targetKey":"customer.fiscal.tax_identifier"}""",
                StringComparison.Ordinal
            );

        var staff = await FormMappingTestDefinitions.PublishAsync(
            FormMappingTestDefinitions.Template(definition),
            FormTemplateHandlerTestDoubles.Publisher,
            new MemoryFormTemplateVersionRepository()
        );
        var admin = await FormMappingTestDefinitions.PublishAsync(
            FormMappingTestDefinitions.Template(definition),
            FormMappingTestDefinitions.Admin,
            new MemoryFormTemplateVersionRepository()
        );

        Assert.Equal(
            [("mapping.permission_denied", "/mappings/7/targetKey")],
            staff.Value.Issues.Select(issue => (issue.Code, issue.Path))
        );
        Assert.True(admin.Value.Published);
    }

    [Fact]
    public async Task Validate_reports_mapping_coverage_for_the_caller()
    {
        var definition = FormMappingTestDefinitions.Onboarding.Replace(
            """{"componentId":"fd_email0001","targetKey":"customer.primary_email","transform":"lowercase"},""",
            string.Empty,
            StringComparison.Ordinal
        );
        var template = FormMappingTestDefinitions.Template(definition);

        var withCoverage = await ValidateFormTemplateHandler.Handle(
            new(template.TenantId, template.Id, FormTemplateHandlerTestDoubles.Publisher),
            FormTemplateHandlerTestDoubles.Engine(),
            new MemoryFormTemplateRepository(template),
            FormTemplateHandlerTestDoubles.TargetCatalog(),
            default
        );
        var structureOnly = await ValidateFormTemplateHandler.Handle(
            new(template.TenantId, template.Id),
            FormTemplateHandlerTestDoubles.Engine(),
            new MemoryFormTemplateRepository(template),
            FormTemplateHandlerTestDoubles.TargetCatalog(),
            default
        );

        Assert.Equal("mapping.required_target_missing", Assert.Single(withCoverage.Value.Issues).Code);
        Assert.Empty(structureOnly.Value.Issues);
    }
}
