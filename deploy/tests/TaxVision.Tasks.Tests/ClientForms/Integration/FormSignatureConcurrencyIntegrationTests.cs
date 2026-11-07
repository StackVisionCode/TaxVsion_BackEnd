using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.ValueObjects;
using TaxVision.Tasks.ClientForms.Domain.Versions;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Tests.ClientForms.Integration;

/// <summary>
/// TSK-4 contra SQL real: la revisión no tiene <c>RowVersion</c>, así que dos consumidores de firma solo chocan si
/// la transición toca la raíz. Base temporal propia.
/// </summary>
public sealed class FormSignatureConcurrencyIntegrationTests : IAsyncLifetime
{
    private const int Revision = 1;
    private static readonly DateTime Now = DateTime.UtcNow;

    private readonly TasksTemporaryDatabase _database = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _responsible = Guid.NewGuid();

    public Task InitializeAsync() => _database.CreateAsync();

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task CompletedAndSealedInParallel_EndsSealed()
    {
        var requestId = await SeedSignedAsync();
        var sealedFileId = Guid.NewGuid();
        await using var sealing = _database.NewContext(_tenantId);
        await using var completing = _database.NewContext(_tenantId);
        var sealedRequest = await LoadAsync(sealing, requestId);
        var completedRequest = await LoadAsync(completing, requestId);

        Assert.True(
            sealedRequest.MarkSignatureSealed(Revision, sealedFileId, new string('c', 64), null).IsSuccess
        );
        Assert.True(completedRequest.MarkSignatureCompleted(Revision).IsSuccess);
        await sealing.SaveChangesAsync();

        var conflict = await Assert.ThrowsAsync<ConflictException>(() => completing.SaveChangesAsync());

        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);
        await using var read = _database.NewContext(_tenantId);
        var stored = await LoadAsync(read, requestId);
        Assert.Equal(FormRevisionSignatureStatus.Sealed, stored.RevisionSignatureStatus(Revision));
        Assert.Equal(sealedFileId, stored.LatestRevision?.SealedFileId);
    }

    private async Task<Guid> SeedSignedAsync()
    {
        var template = FormTemplate
            .Create(
                _tenantId,
                FormTemplateName.Create("TSK-4").Value,
                null,
                ClientFormsTestDocuments.Definition("{\"schemaVersion\":1,\"pages\":[]}"),
                ClientFormsTestDocuments.Hash(new string('0', 64)),
                FormPurpose.ClientIntake,
                _responsible,
                Now
            )
            .Value;
        var version = Version(template.Id);
        var participant = FormParticipant
            .Create(_tenantId, "tsk4@example.test", "TSK4", null, FormParticipantSource.DirectedRequest, Now)
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
                FormRequestOrigin.Directed,
                null,
                _responsible,
                _responsible,
                Now
            )
            .Value;
        var answers = new PlainFormAnswersProtector().Protect(FormActionTestDoubles.Answers);
        request.Submit(answers, [], [], FormActor.ExternalSession(participant.Id, Guid.NewGuid()), Now);
        Assert.True(request.MarkSignatureRequesting(Revision, Guid.NewGuid(), new string('b', 64)).IsSuccess);
        Assert.True(request.LinkSignatureRequest(Revision, Guid.NewGuid()).IsSuccess);
        Assert.True(request.MarkSignatureSigned(Revision).IsSuccess);

        await using var db = _database.NewContext(_tenantId);
        db.AddRange(template, version, participant, request);
        await db.SaveChangesAsync();
        return request.Id;
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

    private static Task<FormRequest> LoadAsync(TasksDbContext db, Guid requestId) =>
        db
            .FormRequests.IgnoreQueryFilters()
            .Include(request => request.Submission!)
            .ThenInclude(submission => submission.Revisions)
            .SingleAsync(request => request.Id == requestId);
}
