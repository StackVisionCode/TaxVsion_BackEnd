using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.ClientRequests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.Infrastructure.Persistence;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;

namespace TaxVision.Tasks.Tests.Integration;

/// <summary>
/// Las consultas del retiro contra SQL Server real (timers vía la tarea dueña, responsable efectivo, columna
/// FollowUpUserId): esas traducciones solo fallan en SQL. Tenant nuevo, sin datos: basta con que traduzcan.
/// </summary>
public sealed class OffboardingQueriesTranslationTests
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

    [Fact]
    public async Task Running_timer_queries_translate_to_sql()
    {
        await using var db = CreateContext();
        var repository = new TaskRepository(db);

        Assert.Empty(await repository.ListWithRunningTimersOfAsync(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(0, await repository.CountRunningTimersOfAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public async Task Client_request_staff_recipient_queries_translate_to_sql()
    {
        await using var db = CreateContext();
        var repository = Internal<IClientRequestRepository>(db, "Persistence.Repositories.ClientRequestRepository");

        Assert.Empty(await repository.ListOpenForStaffRecipientAsync(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(0, await repository.CountOpenForStaffRecipientAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public async Task Client_forms_responsible_queries_translate_to_sql()
    {
        await using var db = CreateContext();
        var links = Internal<IFormIntakeLinkRepository>(
            db,
            "ClientForms.Persistence.Repositories.FormIntakeLinkRepository"
        );
        var requests = Internal<IFormRequestRepository>(
            db,
            "ClientForms.Persistence.Repositories.FormRequestRepository"
        );

        Assert.Empty(await links.ListNotRevokedByResponsibleAsync(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(0, await links.CountNotRevokedByResponsibleAsync(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Empty(await requests.ListOpenByEffectiveResponsibleAsync(Guid.NewGuid(), Guid.NewGuid(), 10));
        Assert.Equal(0, await requests.CountOpenByEffectiveResponsibleAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    // Las implementaciones son internas; se instancian por su contrato público.
    private static T Internal<T>(TasksDbContext db, string typeSuffix) =>
        (T)
            Activator.CreateInstance(
                typeof(TasksDbContext).Assembly.GetType($"TaxVision.Tasks.Infrastructure.{typeSuffix}", true)!,
                db
            )!;
}
