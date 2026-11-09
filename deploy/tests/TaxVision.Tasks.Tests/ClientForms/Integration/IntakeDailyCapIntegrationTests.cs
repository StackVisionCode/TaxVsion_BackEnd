using BuildingBlocks.Results;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Commands;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.ValueObjects;
using TaxVision.Tasks.ClientForms.Domain.Versions;
using TaxVision.Tasks.Infrastructure.Persistence;
using Wolverine;

namespace TaxVision.Tasks.Tests.ClientForms.Integration;

/// <summary>
/// TSK-6 contra SQL real y el host de Tasks, en base temporal: envíos de captación en paralelo del mismo tenant y el
/// mismo día, por el handler real y su transacción. Las FK al aviso de entrada no entran en lo que se mide.
/// </summary>
public sealed class IntakeDailyCapIntegrationTests(IntakeHostFixture host) : IClassFixture<IntakeHostFixture>
{
    private const int Parallel = 6;
    private static readonly DateTime Now = DateTime.UtcNow;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _responsible = Guid.NewGuid();

    [Fact(Timeout = 120_000)]
    public async Task ParallelSubmissions_DoNotExceedCap()
    {
        var commands = await SeedAsync();

        var outcomes = await Task.WhenAll(commands.Select(SubmitAsync));

        await using var db = host.Database.NewContext(_tenantId);
        var stored = await db
            .FormRequests.IgnoreQueryFilters()
            .CountAsync(request => request.TenantId == _tenantId && request.CurrentRevisionNumber > 0);
        Assert.Equal(IntakeHostFixture.DailyCap, stored);
        Assert.Equal(IntakeHostFixture.DailyCap, outcomes.Count(outcome => outcome.IsSuccess));
        Assert.All(
            outcomes.Where(outcome => outcome.IsFailure),
            outcome => Assert.Equal(FormParticipantErrors.IntakeLimitReached.Code, outcome.Error.Code)
        );
    }

    // El cupo vive en la transacción del envío: si el envío se revierte, vuelve.
    [Fact(Timeout = 120_000)]
    public async Task RolledBackSubmission_ReturnsTheSlot()
    {
        using var scope = host.Factory.Services.CreateScope();
        var links = scope.ServiceProvider.GetRequiredService<IFormIntakeLinkRepository>();
        var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            Assert.True(await links.TryConsumeDailyIntakeAsync(_tenantId, Now, 1));
            await transaction.RollbackAsync();
        }

        Assert.True(await links.TryConsumeDailyIntakeAsync(_tenantId, Now, 1));
        Assert.False(await links.TryConsumeDailyIntakeAsync(_tenantId, Now, 1));
    }

    private async Task<Result<FormSubmitOutcome>> SubmitAsync(SubmitPortalFormRequestCommand command)
    {
        using var scope = host.Factory.Services.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result<FormSubmitOutcome>>(command);
    }

    private async Task<IReadOnlyList<SubmitPortalFormRequestCommand>> SeedAsync()
    {
        using var scope = host.Factory.Services.CreateScope();
        var protector = scope.ServiceProvider.GetRequiredService<IAnswersProtector>();
        var template = FormTemplate
            .Create(
                _tenantId,
                FormTemplateName.Create("TSK-6").Value,
                null,
                ClientFormsTestDocuments.Definition("{\"schemaVersion\":1,\"pages\":[]}"),
                ClientFormsTestDocuments.Hash(new string('0', 64)),
                FormPurpose.ClientIntake,
                _responsible,
                Now
            )
            .Value;
        var version = Version(template.Id);

        await using var db = host.Database.NewContext(_tenantId);
        db.AddRange(template, version);
        var seeded = new List<(FormRequest Request, ParticipantContext Context)>();
        for (var i = 0; i < Parallel; i++)
        {
            // Un enlace por envío: en el mismo enlace ya choca su RowVersion. El tope es del tenant.
            var link = FormIntakeLink
                .Create(
                    _tenantId,
                    $"Intake {i}",
                    template.Id,
                    version.Id,
                    Guid.NewGuid(),
                    1,
                    Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
                    "abcd",
                    "cipher",
                    1,
                    null,
                    null,
                    _responsible,
                    _responsible,
                    Now
                )
                .Value;
            var participant = FormParticipant
                .Create(_tenantId, $"tsk6-{i}@example.test", "TSK6", null, FormParticipantSource.IntakeLink, Now)
                .Value;
            participant.MarkVerified(Now);
            var request = FormRequest
                .Create(
                    _tenantId,
                    FormParticipantRef.External(participant.Id),
                    version,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    FormRequestOrigin.IntakeLink,
                    link.Id,
                    _responsible,
                    _responsible,
                    Now
                )
                .Value;
            var context = ParticipantContext.External(_tenantId, participant.Id, request.Id, Guid.NewGuid());
            var saved = request.SaveDraft(protector.Protect(FormActionTestDoubles.Answers), 100, context.Actor, Now);
            Assert.True(saved.IsSuccess, saved.IsFailure ? saved.Error.Code : null);
            db.AddRange(link, participant, request);
            seeded.Add((request, context));
        }
        await db.SaveChangesAsync();

        return seeded
            .Select(item => new SubmitPortalFormRequestCommand(
                item.Context,
                item.Request.Id,
                item.Request.RowVersion,
                Now
            ))
            .ToList();
    }

    private FormTemplateVersion Version(Guid templateId)
    {
        var engine = FormTemplateHandlerTestDoubles.Engine();
        var definition = engine.ParseDefinition(FormMappingTestDefinitions.Onboarding).Value;
        var canonical = engine.Canonicalize(definition);
        var rules = engine.CanonicalizeRuleSets(definition);
        return FormTemplateVersion.Create(
            _tenantId,
            templateId,
            1,
            "Client onboarding",
            FormPurpose.ClientIntake,
            FormPresentation.Wizard,
            ClientFormsTestDocuments.Definition(canonical.Json),
            ClientFormsTestDocuments.Hash(canonical.Sha256),
            7,
            _responsible,
            Now,
            FormVersionRuleSets.Create(rules.MappingSetJson, rules.ActionSetJson, 1).Value
        );
    }
}

/// <summary>Host real de Tasks sobre una base temporal, sin transportes ni jobs propios.</summary>
public sealed class IntakeHostFixture : IAsyncLifetime
{
    public const int DailyCap = 3;

    private HostFactory? _factory;

    internal TasksTemporaryDatabase Database { get; } = new();
    internal WebApplicationFactory<Program> Factory => _factory!;

    public async Task InitializeAsync()
    {
        await Database.CreateAsync();
        await using (var db = Database.NewContext(Guid.Empty))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE FormIntakeLinks NOCHECK CONSTRAINT ALL;");
        _factory = new HostFactory(Database.ConnectionString);
        _ = _factory.Server;
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        await Database.DropAsync();
    }

    private sealed class HostFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("ClientForms:External:MaxIntakeSubmissionsPerTenantPerDay", DailyCap.ToString());
            builder.ConfigureTestServices(services =>
            {
                services.DisableAllExternalWolverineTransports();
                // Los jobs propios corren contra la base: fuera mientras dura la prueba.
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
