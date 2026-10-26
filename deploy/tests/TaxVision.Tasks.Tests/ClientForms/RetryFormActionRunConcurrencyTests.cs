using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Tasks.ClientForms.Application.Actions;
using TaxVision.Tasks.ClientForms.Application.Actions.Commands;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.ValueObjects;
using TaxVision.Tasks.ClientForms.Domain.Versions;
using TaxVision.Tasks.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Tracking;

namespace TaxVision.Tasks.Tests.ClientForms;

/// <summary>
/// T-8 (P-17.5) contra SQL Server real: dos reintentos simultáneos sobre el mismo run rechazado.
/// Solo uno emite el comando a Customer; el otro termina en 409 (el run ya no es reintentable o el
/// índice único del intento lo frena). Host real de Tasks con los transportes externos anulados,
/// un tenant propio y limpieza al final.
/// </summary>
public sealed class RetryFormActionRunConcurrencyTests
    : IClassFixture<RetryFormActionRunConcurrencyTests.TasksApiFactory>
{
    private const int Rounds = 5;
    private static readonly DateTime Now = DateTime.UtcNow;
    private readonly TasksApiFactory factory;

    public RetryFormActionRunConcurrencyTests(TasksApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Two_concurrent_retries_emit_once_and_the_other_gets_a_conflict()
    {
        var tenantId = Guid.NewGuid();
        var responsible = Guid.NewGuid();
        try
        {
            for (var round = 1; round <= Rounds; round++)
            {
                var (requestId, runId) = await SeedRejectedRunAsync(tenantId, responsible, round);
                var outcomes = new List<object>();
                var session = await factory
                    .Services.GetRequiredService<IHost>()
                    .TrackActivity()
                    .Timeout(TimeSpan.FromSeconds(60))
                    .DoNotAssertOnExceptionsDetected()
                    .ExecuteAndWaitAsync(
                        (Func<IMessageContext, Task>)(
                            async _ =>
                            {
                                var command = new RetryFormActionRunCommand(
                                    tenantId,
                                    responsible,
                                    true,
                                    requestId,
                                    runId,
                                    Now
                                );
                                outcomes.AddRange(await Task.WhenAll(RetryAsync(command), RetryAsync(command)));
                            }
                        )
                    );

                var emitted = session.Sent.MessagesOf<CustomerCreationFromFormRequestedIntegrationEvent>().ToArray();
                Assert.Single(emitted);
                Assert.Single(outcomes, outcome => outcome is Result<FormActionRunResponse> { IsSuccess: true });
                Assert.Single(outcomes, IsConflict);
                Assert.Equal(1, await CountRetriedRunsAsync(requestId));
            }
        }
        finally
        {
            await CleanUpAsync(tenantId);
        }
    }

    // 409 = el run ya no es reintentable, conflicto de concurrencia o el índice único del intento.
    private static bool IsConflict(object outcome) =>
        outcome switch
        {
            Result<FormActionRunResponse> { IsFailure: true } failure =>
                BuildingBlocks.Web.Results.ErrorHttpMapping.ToHttpStatusCode(failure.Error) == 409,
            ConflictException or DbUpdateException => true,
            _ => false,
        };

    private async Task<object> RetryAsync(RetryFormActionRunCommand command)
    {
        using var scope = factory.Services.CreateScope();
        try
        {
            return await scope
                .ServiceProvider.GetRequiredService<IMessageBus>()
                .InvokeAsync<Result<FormActionRunResponse>>(command);
        }
        catch (Exception exception) when (exception is ConflictException or DbUpdateException)
        {
            return exception;
        }
    }

    private async Task<(Guid RequestId, Guid RunId)> SeedRejectedRunAsync(Guid tenantId, Guid responsible, int round)
    {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var template = FormTemplate
            .Create(
                tenantId,
                FormTemplateName.Create($"T-8 {round}").Value,
                null,
                ClientFormsTestDocuments.Definition("{\"schemaVersion\":1,\"pages\":[]}"),
                ClientFormsTestDocuments.Hash(new string('0', 64)),
                FormPurpose.ClientIntake,
                responsible,
                Now
            )
            .Value;
        var version = Version(tenantId, template.Id, responsible);
        var participant = FormParticipant
            .Create(tenantId, $"t8-{round}@example.test", "T8", null, FormParticipantSource.DirectedRequest, Now)
            .Value;
        participant.MarkVerified(Now);
        var request = FormRequest
            .Create(
                tenantId,
                FormParticipantRef.External(participant.Id),
                version,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                FormRequestOrigin.Directed,
                null,
                responsible,
                responsible,
                Now
            )
            .Value;
        var answers = services.GetRequiredService<IAnswersProtector>().Protect(FormActionTestDoubles.Answers);
        request.Submit(answers, [], [], FormActor.ExternalSession(participant.Id, Guid.NewGuid()), Now);

        var db = services.GetRequiredService<TasksDbContext>();
        db.AddRange(template, version, participant, request);
        await db.SaveChangesAsync();

        var run = await services
            .GetRequiredService<FormActionPlanner>()
            .RequestCustomerCreationAsync(request, null, Now, default);
        request.RejectAction(run.Value.Run.CommandId!.Value, "Customer.CreatorNotInDirectory", null, null, Now);
        await db.SaveChangesAsync();
        return (request.Id, run.Value.Run.Id);
    }

    // La definición de onboarding de las pruebas de acciones, colgada de una plantilla real.
    private static FormTemplateVersion Version(Guid tenantId, Guid templateId, Guid byUserId)
    {
        var engine = FormTemplateHandlerTestDoubles.Engine();
        var definition = engine.ParseDefinition(FormMappingTestDefinitions.Onboarding).Value;
        var canonical = engine.Canonicalize(definition);
        var rules = engine.CanonicalizeRuleSets(definition);
        return FormTemplateVersion.Create(
            tenantId,
            templateId,
            1,
            "Client onboarding",
            FormPurpose.ClientIntake,
            FormPresentation.Wizard,
            ClientFormsTestDocuments.Definition(canonical.Json),
            ClientFormsTestDocuments.Hash(canonical.Sha256),
            7,
            byUserId,
            Now,
            FormVersionRuleSets.Create(rules.MappingSetJson, rules.ActionSetJson, 1).Value
        );
    }

    private async Task<int> CountRetriedRunsAsync(Guid requestId)
    {
        await using var connection = new SqlConnection(ConnectionString());
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT COUNT(*) FROM FormActionRuns WHERE FormRequestId = @request AND Attempt = 2",
            connection
        );
        command.Parameters.AddWithValue("@request", requestId);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    // Solo filas del tenant de la prueba; las hijas de FormRequests caen en cascada.
    private async Task CleanUpAsync(Guid tenantId)
    {
        await using var connection = new SqlConnection(ConnectionString());
        await connection.OpenAsync();
        foreach (
            var table in new[]
            {
                "FormAuditEntries",
                "FormRequests",
                "FormParticipants",
                "FormTemplateVersions",
                "FormTemplates",
            }
        )
        {
            await using var command = new SqlCommand($"DELETE FROM {table} WHERE TenantId = @tenant", connection);
            command.Parameters.AddWithValue("@tenant", tenantId);
            await command.ExecuteNonQueryAsync();
        }
    }

    private string ConnectionString()
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<TasksDbContext>().Database.GetConnectionString()!;
    }

    public sealed class TasksApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.DisableAllExternalWolverineTransports();
                // Los jobs propios corren contra la base real: fuera mientras dura la prueba.
                foreach (
                    var job in services
                        .Where(descriptor =>
                            descriptor.ServiceType == typeof(IHostedService)
                            && descriptor.ImplementationType?.Namespace?.StartsWith(
                                "TaxVision.",
                                StringComparison.Ordinal
                            ) == true
                        )
                        .ToList()
                )
                    services.Remove(job);
            });
        }
    }
}
