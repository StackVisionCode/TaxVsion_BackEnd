using System.Reflection;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Web.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
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
}
