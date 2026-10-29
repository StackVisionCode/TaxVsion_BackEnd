using System.Security.Cryptography;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.ValueObjects;
using TaxVision.Tasks.ClientForms.Domain.Versions;
using TaxVision.Tasks.Infrastructure.ClientForms.Security;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Tests.ClientForms.Integration;

/// <summary>
/// X-9.6 — el job re-cifra ~10.000 filas contra SQL real, con interrupción y reanudación (idempotente y
/// reanudable). Arnés acotado: DbContext directo, sin WAF ni hosted services. Tenant propio y limpieza al
/// final. Las filas se siembran en una versión de clave (2) con clave de prueba y se re-cifran a otra (1),
/// ambas de prueba: el barrido global no toca los datos del app, que están en la versión 1 con su clave y
/// por eso quedan fuera del filtro (su versión ya es la actual del barrido).
/// </summary>
[Collection(nameof(TasksCiphertextSqlCollection))]
public sealed class FormReEncryptionIntegrationTests
{
    private const int RowCount = 10_000;
    private const string Payload = "{\"answers\":{\"ssn\":\"masked\"}}";
    private const short SeedVersion = 2;
    private const short TargetVersion = 1;
    private static readonly DateTime Now = DateTime.UtcNow;

    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("ConnectionStrings__Default")
        ?? "Server=localhost,1433;Database=TaxVision_Tasks;Trusted_Connection=True;TrustServerCertificate=True";

    private static readonly string TargetKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static readonly string SeedKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task The_job_reencrypts_ten_thousand_rows_resuming_after_an_interruption()
    {
        try
        {
            await SeedAsync();

            // Primera pasada: se cancela apenas se ve avance, dejando filas sin re-cifrar.
            using (var cts = new CancellationTokenSource())
            {
                var run = Task.Run(() => CreateStore().ReEncryptToCurrentAsync(200, cts.Token));
                await WaitUntilProgressAsync(cts);
                try
                {
                    await run;
                }
                catch (OperationCanceledException) { }
            }

            Assert.True(
                await CountAtVersionAsync(SeedVersion) < RowCount,
                "Expected the interrupted run to re-encrypt at least one batch."
            );

            // Reanudación: termina lo que faltaba. Idempotente: las ya hechas salen del filtro.
            await CreateStore().ReEncryptToCurrentAsync(500, CancellationToken.None);

            Assert.Equal(0, await CountAtVersionAsync(SeedVersion));
            Assert.Equal(RowCount, await CountAtVersionAsync(TargetVersion));
            await AssertASampleDecryptsAsync();

            // Correr de nuevo no reescribe las filas ya en la versión actual.
            await CreateStore().ReEncryptToCurrentAsync(500, CancellationToken.None);
            Assert.Equal(RowCount, await CountAtVersionAsync(TargetVersion));
        }
        finally
        {
            await CleanupAsync();
        }
    }

    private async Task SeedAsync()
    {
        var seedAnswers = AnswersProtectorFor(SeedKey, SeedVersion, null, null);
        await using var db = CreateContext();

        var template = FormTemplate
            .Create(
                _tenantId,
                FormTemplateName.Create("Re-encryption").Value,
                null,
                ClientFormsTestDocuments.Definition("{\"schemaVersion\":1,\"pages\":[]}"),
                ClientFormsTestDocuments.Hash(new string('0', 64)),
                FormPurpose.ClientIntake,
                Guid.NewGuid(),
                Now
            )
            .Value;
        var version = BuildVersion(template.Id);
        db.Add(template);
        db.Add(version);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var responsible = Guid.NewGuid();
        var batch = new List<object>(2000);
        for (var index = 0; index < RowCount; index++)
        {
            var participant = FormParticipant
                .Create(_tenantId, $"reenc-{index}@example.test", "N", null, FormParticipantSource.DirectedRequest, Now)
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
                    seedAnswers.Protect(Payload),
                    FormRequestOrigin.Directed,
                    null,
                    responsible,
                    responsible,
                    Now
                )
                .Value;
            batch.Add(participant);
            batch.Add(request);
            if (batch.Count >= 2000)
            {
                db.AddRange(batch);
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
                batch.Clear();
            }
        }
        if (batch.Count > 0)
        {
            db.AddRange(batch);
            await db.SaveChangesAsync();
        }
    }

    private async Task WaitUntilProgressAsync(CancellationTokenSource cts)
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            if (await CountAtVersionAsync(SeedVersion) < RowCount)
            {
                await cts.CancelAsync();
                return;
            }
            await Task.Delay(10);
        }
        await cts.CancelAsync();
    }

    private async Task<int> CountAtVersionAsync(short version)
    {
        await using var db = CreateContext();
        return await (
            from submission in db.FormSubmissions.IgnoreQueryFilters()
            join request in db.FormRequests.IgnoreQueryFilters() on submission.FormRequestId equals request.Id
            where request.TenantId == _tenantId && submission.DraftAnswers!.KeyVersion == version
            select submission.Id
        ).CountAsync();
    }

    private async Task AssertASampleDecryptsAsync()
    {
        await using var db = CreateContext();
        var sample = await (
            from submission in db.FormSubmissions.IgnoreQueryFilters().AsNoTracking()
            join request in db.FormRequests.IgnoreQueryFilters() on submission.FormRequestId equals request.Id
            where request.TenantId == _tenantId
            select submission
        ).FirstAsync();
        Assert.Equal(
            Payload,
            AnswersProtectorFor(TargetKey, TargetVersion, SeedKey, SeedVersion).Unprotect(sample.DraftAnswers!)
        );
    }

    private async Task CleanupAsync()
    {
        await using var db = CreateContext();
        // El borrado por tenant: FormSubmissions cae en cascada con FormRequests.
        await db.FormRequests.IgnoreQueryFilters().Where(r => r.TenantId == _tenantId).ExecuteDeleteAsync();
        await db.FormParticipants.IgnoreQueryFilters().Where(p => p.TenantId == _tenantId).ExecuteDeleteAsync();
        await db.FormTemplateVersions.IgnoreQueryFilters().Where(v => v.TenantId == _tenantId).ExecuteDeleteAsync();
        await db.FormTemplates.IgnoreQueryFilters().Where(t => t.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    private FormCiphertextReEncryptionStore CreateStore()
    {
        var options = OptionsFor(TargetKey, TargetVersion, SeedKey, SeedVersion);
        var rotating = new AesGcmRotatingSecretProtector(options);
        return new FormCiphertextReEncryptionStore(
            CreateContext(),
            new AnswersProtector(rotating),
            new SensitiveValueProtector(rotating),
            new FormExternalAccessSecurity(rotating, options),
            options
        );
    }

    private FormTemplateVersion BuildVersion(Guid templateId)
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
            Guid.NewGuid(),
            Now,
            FormVersionRuleSets.Create(rules.MappingSetJson, rules.ActionSetJson, 1).Value
        );
    }

    private AnswersProtector AnswersProtectorFor(
        string currentKey,
        short currentVersion,
        string? previousKey,
        short? previousVersion
    ) => new(new AesGcmRotatingSecretProtector(OptionsFor(currentKey, currentVersion, previousKey, previousVersion)));

    private static IOptions<RotatingSecretProtectionOptions> OptionsFor(
        string currentKey,
        short currentVersion,
        string? previousKey,
        short? previousVersion
    ) =>
        Options.Create(
            new RotatingSecretProtectionOptions
            {
                MasterKey = currentKey,
                MasterKeyVersion = currentVersion,
                PreviousMasterKey = previousKey,
                PreviousMasterKeyVersion = previousVersion,
            }
        );

    private TasksDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<TasksDbContext>().UseSqlServer(ConnectionString).Options,
            new FixedTenantContext(_tenantId)
        );

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => tenantId != Guid.Empty;

        public void SetTenant(Guid value) { }
    }
}

[CollectionDefinition(nameof(TasksCiphertextSqlCollection), DisableParallelization = true)]
public sealed class TasksCiphertextSqlCollection;
