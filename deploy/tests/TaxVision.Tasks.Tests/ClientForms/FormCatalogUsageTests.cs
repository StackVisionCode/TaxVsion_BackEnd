using System.Reflection;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Api.Controllers.ClientForms;
using TaxVision.Tasks.Api.Filters;
using TaxVision.Tasks.ClientForms.Application.Mapping;
using TaxVision.Tasks.ClientForms.Application.Mapping.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Mapping.Queries;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Versions;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class FormCatalogUsageTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("customer")]
    [InlineData("customer.%")]
    [InlineData("Customer.Kind")]
    public async Task Invalid_target_keys_are_rejected(string? targetKey)
    {
        var result = await GetFormCatalogUsageHandler.Handle(new(targetKey), new FixedUsage(), default);

        Assert.Equal(FormCatalogUsageErrors.InvalidTargetKey, result.Error);
    }

    [Fact]
    public async Task Counts_come_from_the_reader()
    {
        var result = await GetFormCatalogUsageHandler.Handle(new("customer.primary_email"), new FixedUsage(), default);

        Assert.Equal(
            ("customer.primary_email", 3, 2, 1),
            (
                result.Value.TargetKey,
                result.Value.OpenRequests,
                result.Value.ActiveIntakeLinks,
                result.Value.PublishedVersions
            )
        );
    }

    [Fact]
    public async Task Reader_counts_open_requests_and_usable_links_pinned_to_versions_that_map_the_key()
    {
        await using var db = Database();
        var mapped = Version("[{\"componentId\":\"fd_email0001\",\"targetKey\":\"customer.primary_email\"}]");
        var other = Version("[{\"componentId\":\"fd_first0001\",\"targetKey\":\"customer.person.first_name\"}]");
        db.FormTemplateVersions.AddRange(mapped, other);
        db.FormRequests.Add(FormRequestFixtures.CustomerRequest(mapped));
        var completed = FormRequestFixtures.CustomerRequest(mapped);
        FormRequestFixtures.Submit(completed);
        completed.Complete(null, FormRequestFixtures.UserId, FormRequestFixtures.Now);
        db.FormRequests.Add(completed);
        db.FormRequests.Add(FormRequestFixtures.CustomerRequest(other));
        db.FormIntakeLinks.Add(Link(mapped));
        var revoked = Link(mapped);
        revoked.Revoke();
        db.FormIntakeLinks.Add(revoked);
        await db.SaveChangesAsync();

        var counts = await Reader(db).CountAsync("customer.primary_email", FormRequestFixtures.Now);

        Assert.Equal(new FormCatalogUsageCounts(1, 1, 1), counts);
    }

    [Fact]
    public void Endpoint_is_platform_only()
    {
        var controller = typeof(FormCatalogUsageController);

        Assert.Equal("tasks/forms/platform/catalog-usage", controller.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal([ActorType.PlatformAdmin], controller.GetCustomAttribute<AllowActorTypesAttribute>()!.ActorTypes);
        Assert.Equal(
            "perm:" + TasksPermissions.FormsPlatformManage,
            controller.GetCustomAttribute<HasPermissionAttribute>()!.Policy
        );
        Assert.NotNull(controller.GetCustomAttribute<SkipClientFormsAccessAttribute>());
        Assert.NotNull(controller.GetCustomAttribute<ClientFormsEndpointAttribute>());
    }

    private static FormTemplateVersion Version(string mappingSetJson)
    {
        var version = FormRequestFixtures.Version();
        typeof(FormTemplateVersion)
            .GetProperty(nameof(FormTemplateVersion.MappingSetJson))!
            .SetValue(version, mappingSetJson);
        return version;
    }

    private static FormIntakeLink Link(FormTemplateVersion version) =>
        FormIntakeLink
            .Create(
                version.TenantId,
                "Website",
                version.FormTemplateId,
                version.Id,
                Guid.NewGuid(),
                1,
                new string('h', 64),
                "abcd",
                "cipher",
                1,
                null,
                null,
                FormRequestFixtures.UserId,
                FormRequestFixtures.UserId,
                FormRequestFixtures.Now
            )
            .Value;

    private static TasksDbContext Database() =>
        new(
            new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenantContext()
        );

    // La implementación es interna; se instancia por su contrato público.
    private static IFormCatalogUsageReader Reader(TasksDbContext db) =>
        (IFormCatalogUsageReader)
            Activator.CreateInstance(
                typeof(TasksDbContext).Assembly.GetType(
                    "TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Repositories.FormCatalogUsageReader",
                    throwOnError: true
                )!,
                db
            )!;

    private sealed class FixedUsage : IFormCatalogUsageReader
    {
        public Task<FormCatalogUsageCounts> CountAsync(
            string targetKey,
            DateTime nowUtc,
            CancellationToken ct = default
        ) => Task.FromResult(new FormCatalogUsageCounts(3, 2, 1));
    }

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
