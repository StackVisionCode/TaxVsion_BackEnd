using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Tests.ClientForms.Integration;

/// <summary>
/// Las consultas de plantillas contra SQL Server real: ordenan y filtran por la columna del nombre
/// (propiedad sombra), y esa traducción solo falla en SQL. InMemory contestaría bien igual.
/// </summary>
public sealed class FormTemplateRepositoryQueryTests
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("ConnectionStrings__Default")
        ?? "Server=localhost,1433;Database=TaxVision_Tasks;Trusted_Connection=True;TrustServerCertificate=True";

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    private static TasksDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<TasksDbContext>().UseSqlServer(ConnectionString).Options,
            new NoTenantContext()
        );

    [Theory]
    [InlineData(null)]
    [InlineData("intake")]
    public async Task Assignable_templates_translate_to_sql(string? search)
    {
        await using var db = CreateContext();

        var templates = await Repository(db).ListAssignableAsync(Guid.NewGuid(), search);

        Assert.Empty(templates);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("intake")]
    public async Task Template_list_search_translates_to_sql(string? search)
    {
        await using var db = CreateContext();

        var page = await Repository(db).ListAsync(Guid.NewGuid(), null, null, search, 1, 10);

        Assert.Equal(0, page.TotalCount);
    }

    // La implementación es interna; se instancia por su contrato público.
    private static IFormTemplateRepository Repository(TasksDbContext db) =>
        (IFormTemplateRepository)
            Activator.CreateInstance(
                typeof(TasksDbContext).Assembly.GetType(
                    "TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Repositories.FormTemplateRepository",
                    throwOnError: true
                )!,
                db
            )!;
}
