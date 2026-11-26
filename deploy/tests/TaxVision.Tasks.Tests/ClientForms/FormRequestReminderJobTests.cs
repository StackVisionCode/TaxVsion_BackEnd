using System.Reflection;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.Domain.RateLimiting;
using TaxVision.Tasks.Infrastructure.Persistence;
using static TaxVision.Tasks.Tests.ClientForms.FormRequestFixtures;

namespace TaxVision.Tasks.Tests.ClientForms;

// El job es el origen de la traza: sus avisos llevan un id de correlación propio.
public sealed class FormRequestReminderJobTests
{
    private const string JobType = "TaxVision.Tasks.Infrastructure.ClientForms.FormRequestReminderJob";

    [Fact]
    public async Task Job_publishes_with_a_correlation_id()
    {
        var (outbox, recorder) = RecordingDbContextOutbox.Create();
        var services = new ServiceCollection()
            .AddSingleton<IFormRequestRepository>(new SingleFormRequestRepository(CustomerRequest(), CustomerRequest()))
            .AddSingleton(outbox)
            .AddScoped<ICorrelationContext, CorrelationContext>()
            .BuildServiceProvider();
        var type = typeof(TasksDbContext).Assembly.GetType(JobType, throwOnError: true)!;
        var job = Activator.CreateInstance(
            type,
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ClientFormsOptions()),
            Activator.CreateInstance(typeof(NullLogger<>).MakeGenericType(type))
        )!;

        var sweep = type.GetMethod("SweepAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)sweep.Invoke(job, [new ClientFormsReminderOptions(), CancellationToken.None])!;

        var events = recorder.Flushed.OfType<FormRequestReminderDueIntegrationEvent>().ToList();
        Assert.Equal(2, events.Count);
        Assert.All(events, evt => Assert.False(string.IsNullOrWhiteSpace(evt.CorrelationId)));
        Assert.Single(events.Select(evt => evt.CorrelationId).Distinct());
    }

    // Oficina sin el módulo: sus solicitudes no salen en la consulta y no tapan a las demás oficinas.
    [Fact]
    public async Task Reminder_candidates_skip_offices_without_the_module()
    {
        await using var db = new TasksDbContext(
            new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenantContext()
        );
        var suspended = Guid.NewGuid();
        var active = Guid.NewGuid();
        var withoutProjection = Guid.NewGuid();
        db.TenantPlanCodeProjections.AddRange(Projection(suspended, "wallet"), Projection(active, "planner"));
        db.FormRequests.AddRange(
            CustomerRequest(tenantId: suspended),
            CustomerRequest(tenantId: active),
            CustomerRequest(tenantId: withoutProjection)
        );
        await db.SaveChangesAsync();
        var repository = (IFormRequestRepository)
            Activator.CreateInstance(
                typeof(TasksDbContext).Assembly.GetType(
                    "TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Repositories.FormRequestRepository",
                    throwOnError: true
                )!,
                db
            )!;

        var candidates = await repository.ListReminderCandidatesAsync(Now.AddDays(30), 5, 200);

        Assert.Equal(new[] { active, withoutProjection }.Order(), candidates.Select(item => item.TenantId).Order());
    }

    private static TenantPlanCodeProjection Projection(Guid tenantId, params string[] modules)
    {
        var projection = TenantPlanCodeProjection.Create(tenantId, "pro", 1);
        projection.ApplyIfNewer("pro", 1, modules);
        return projection;
    }

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
